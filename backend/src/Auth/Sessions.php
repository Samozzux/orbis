<?php
namespace Orbis\Api\Auth;

use Orbis\Api\Http\HttpError;
use PDO;

/**
 * Refresh token per dispositivo (docs/06 §4): casuali a 256 bit, salvati solo come hash, durata 90 giorni,
 * ruotati a ogni rinnovo. Se torna un token già sostituito (copiato o rubato) si chiude la sessione di quel dispositivo.
 */
class Sessions
{
    const REFRESH_DAYS = 90;

    /** Nuova sessione: chiude quelle già aperte dallo stesso dispositivo. */
    public static function create(PDO $pdo, $userId, array $device)
    {
        self::revokeDevice($pdo, $userId, $device['id']);
        return self::insert($pdo, $userId, $device['id'], $device['name'], $device['platform']);
    }

    /**
     * Rinnovo: restituisce array(utente, nuova sessione). Il token usato viene revocato;
     * se era già stato revocato (riuso) si revoca tutta la catena del dispositivo.
     */
    public static function rotate(PDO $pdo, $plain)
    {
        if (!is_string($plain) || !preg_match('/^[A-Za-z0-9_-]{43}$/', $plain)) {
            throw new HttpError(401, 'invalid_refresh', 'Sessione non valida: serve un nuovo accesso.');
        }
        $pdo->beginTransaction();
        try {
            $st = $pdo->prepare('SELECT id, user_id, device_id, device_name, platform, revoked_at, expires_at < UTC_TIMESTAMP() AS expired
                FROM orb_refresh_tokens WHERE token_hash = ? FOR UPDATE');
            $st->execute(array(hash('sha256', $plain)));
            $row = $st->fetch();
            if (!$row) {
                $pdo->commit();
                throw new HttpError(401, 'invalid_refresh', 'Sessione non valida: serve un nuovo accesso.');
            }
            if ($row['revoked_at'] !== null) {
                self::revokeDevice($pdo, (int)$row['user_id'], $row['device_id']);
                $pdo->commit();
                error_log('[orbis] refresh token riusato: sessione del dispositivo chiusa (utente ' . (int)$row['user_id'] . ')');
                throw new HttpError(401, 'refresh_reused', 'Sessione chiusa per sicurezza: serve un nuovo accesso.');
            }
            if ((int)$row['expired'] === 1) {
                $pdo->commit();
                throw new HttpError(401, 'refresh_expired', 'Sessione scaduta: serve un nuovo accesso.');
            }
            $user = Auth::findUser($pdo, (int)$row['user_id']);
            if ($user === null || $user['status'] !== 'active') {
                self::revokeDevice($pdo, (int)$row['user_id'], $row['device_id']);
                $pdo->commit();
                throw new HttpError(401, 'invalid_refresh', 'Account non disponibile.');
            }
            $session = self::insert($pdo, (int)$row['user_id'], $row['device_id'], $row['device_name'], $row['platform']);
            $pdo->prepare('UPDATE orb_refresh_tokens SET revoked_at = UTC_TIMESTAMP(), replaced_by = ? WHERE id = ?')
                ->execute(array($session['id'], $row['id']));
            $pdo->commit();
            return array($user, $session);
        } catch (\Exception $e) {
            if ($pdo->inTransaction()) {
                $pdo->rollBack();
            }
            throw $e;
        }
    }

    /** Chiude la sessione del token indicato (uscita). Restituisce true se il token esisteva. */
    public static function revokeToken(PDO $pdo, $plain)
    {
        if (!is_string($plain) || $plain === '') {
            return false;
        }
        $st = $pdo->prepare('SELECT user_id, device_id FROM orb_refresh_tokens WHERE token_hash = ?');
        $st->execute(array(hash('sha256', $plain)));
        $row = $st->fetch();
        if (!$row) {
            return false;
        }
        self::revokeDevice($pdo, (int)$row['user_id'], $row['device_id']);
        return true;
    }

    public static function revokeDevice(PDO $pdo, $userId, $deviceId)
    {
        $pdo->prepare('UPDATE orb_refresh_tokens SET revoked_at = UTC_TIMESTAMP() WHERE user_id = ? AND device_id = ? AND revoked_at IS NULL')
            ->execute(array($userId, $deviceId));
    }

    /** Sessioni aperte dell'utente, una per dispositivo. */
    public static function active(PDO $pdo, $userId)
    {
        $st = $pdo->prepare('SELECT id, device_id, device_name, platform, created_at, expires_at FROM orb_refresh_tokens
            WHERE user_id = ? AND revoked_at IS NULL AND expires_at > UTC_TIMESTAMP() ORDER BY created_at DESC, id DESC');
        $st->execute(array($userId));
        return $st->fetchAll();
    }

    /** Chiude una sessione dell'utente dal suo id (dall'elenco delle sessioni). */
    public static function revokeById(PDO $pdo, $userId, $id)
    {
        $st = $pdo->prepare('SELECT device_id FROM orb_refresh_tokens WHERE id = ? AND user_id = ?');
        $st->execute(array($id, $userId));
        $row = $st->fetch();
        if (!$row) {
            return false;
        }
        self::revokeDevice($pdo, $userId, $row['device_id']);
        return true;
    }

    private static function insert(PDO $pdo, $userId, $deviceId, $deviceName, $platform)
    {
        $plain = Jwt::b64(random_bytes(32));
        $pdo->prepare('INSERT INTO orb_refresh_tokens (user_id, token_hash, device_id, device_name, platform, created_at, expires_at)
            VALUES (?, ?, ?, ?, ?, UTC_TIMESTAMP(), UTC_TIMESTAMP() + INTERVAL ' . self::REFRESH_DAYS . ' DAY)')
            ->execute(array($userId, hash('sha256', $plain), $deviceId, $deviceName, $platform));
        $id = (int)$pdo->lastInsertId();
        $st = $pdo->prepare('SELECT expires_at FROM orb_refresh_tokens WHERE id = ?');
        $st->execute(array($id));
        return array('id' => $id, 'token' => $plain, 'device_id' => $deviceId, 'expires_at' => $st->fetchColumn());
    }
}
