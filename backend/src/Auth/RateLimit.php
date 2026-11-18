<?php
namespace Orbis\Api\Auth;

use Orbis\Api\Http\HttpError;
use PDO;

/**
 * Limite ai tentativi (docs/06 §4): 10 accessi falliti per IP e username ogni 15 minuti (30 per IP),
 * 5 nuovi account per IP all'ora. L'IP si salva solo come HMAC col segreto del server.
 */
class RateLimit
{
    const WINDOW_MINUTES = 15;
    const MAX_FAILED_PAIR = 10;
    const MAX_FAILED_IP = 30;
    const MAX_REGISTER_HOUR = 5;

    public static function ipHash($ip)
    {
        return hash_hmac('sha256', (string)$ip, Auth::secret());
    }

    public static function checkLogin(PDO $pdo, $ipHash, $username)
    {
        $st = $pdo->prepare('SELECT SUM(username = ?) AS pair, COUNT(*) AS ip FROM orb_login_attempts
            WHERE ip_hash = ? AND kind = \'login\' AND success = 0 AND created_at > UTC_TIMESTAMP() - INTERVAL ' . self::WINDOW_MINUTES . ' MINUTE');
        $st->execute(array($username, $ipHash));
        $row = $st->fetch();
        if ((int)$row['pair'] >= self::MAX_FAILED_PAIR || (int)$row['ip'] >= self::MAX_FAILED_IP) {
            throw new HttpError(429, 'too_many_attempts', 'Troppi tentativi: riprova tra qualche minuto.',
                array('Retry-After: ' . (self::WINDOW_MINUTES * 60)));
        }
    }

    public static function checkRegister(PDO $pdo, $ipHash)
    {
        $st = $pdo->prepare('SELECT COUNT(*) FROM orb_login_attempts
            WHERE ip_hash = ? AND kind = \'register\' AND success = 1 AND created_at > UTC_TIMESTAMP() - INTERVAL 1 HOUR');
        $st->execute(array($ipHash));
        if ((int)$st->fetchColumn() >= self::MAX_REGISTER_HOUR) {
            throw new HttpError(429, 'too_many_attempts', 'Troppi account creati: riprova più tardi.', array('Retry-After: 3600'));
        }
    }

    public static function record(PDO $pdo, $ipHash, $username, $kind, $success)
    {
        $pdo->prepare('INSERT INTO orb_login_attempts (ip_hash, username, kind, success, created_at) VALUES (?, ?, ?, ?, UTC_TIMESTAMP())')
            ->execute(array($ipHash, substr((string)$username, 0, 20), $kind, $success ? 1 : 0));
        // pulizia saltuaria: tentativi più vecchi di un giorno, sessioni scadute da più di 30 giorni
        if (mt_rand(1, 50) === 1) {
            $pdo->exec('DELETE FROM orb_login_attempts WHERE created_at < UTC_TIMESTAMP() - INTERVAL 1 DAY');
            $pdo->exec('DELETE FROM orb_refresh_tokens WHERE expires_at < UTC_TIMESTAMP() - INTERVAL 30 DAY');
        }
    }
}
