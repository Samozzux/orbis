<?php
namespace Orbis\Api\Controllers;

use Orbis\Api\Auth\Auth;
use Orbis\Api\Auth\Cbor;
use Orbis\Api\Auth\RateLimit;
use Orbis\Api\Auth\Sessions;
use Orbis\Api\Auth\WebAuthn;
use Orbis\Api\Config;
use Orbis\Api\Http\HttpError;
use Orbis\Api\Http\Request;
use Orbis\Api\Http\Response;
use PDO;

/**
 * Passkey per la versione web (docs/06 §4): si crea da dentro l'account (dopo l'accesso con la password),
 * poi si entra con Face ID / Touch ID / Windows Hello senza password. Le passkey sono "discoverable":
 * per entrare non serve scrivere il nome utente. L'accesso dà gli stessi token della password.
 */
class PasskeyController
{
    const CHALLENGE_SECONDS = 300;
    const MAX_PASSKEYS = 10;

    public static function registerOptions(Request $request, array $params)
    {
        $user = Auth::user($request);
        $pdo = Auth::pdo();
        $rpId = self::rpId($request);
        list($id, $challenge) = self::newChallenge($pdo, 'register', (int)$user['id']);
        $exclude = array();
        foreach (self::listFor($pdo, (int)$user['id']) as $row) {
            $exclude[] = array('type' => 'public-key', 'id' => $row['credential_id']);
        }
        Response::json(200, array(
            'challengeId' => $id,
            'publicKey' => array(
                'challenge' => $challenge,
                'rp' => array('id' => $rpId, 'name' => 'Orbis'),
                'user' => array('id' => self::userHandle((int)$user['id']), 'name' => $user['username'], 'displayName' => $user['display_name']),
                'pubKeyCredParams' => array(array('type' => 'public-key', 'alg' => WebAuthn::ES256), array('type' => 'public-key', 'alg' => WebAuthn::RS256)),
                'timeout' => self::CHALLENGE_SECONDS * 1000,
                'attestation' => 'none',
                'authenticatorSelection' => array('residentKey' => 'required', 'requireResidentKey' => true, 'userVerification' => 'required'),
                'excludeCredentials' => $exclude,
            ),
        ));
    }

    public static function registerVerify(Request $request, array $params)
    {
        $user = Auth::user($request);
        $pdo = Auth::pdo();
        $challenge = self::takeChallenge($pdo, $request->str('challengeId'), 'register', (int)$user['id']);
        $clientJson = WebAuthn::unb64($request->str('clientDataJSON'));
        $origin = WebAuthn::clientData($clientJson, 'webauthn.create', $challenge);
        list($attestation) = Cbor::decode(WebAuthn::unb64($request->str('attestationObject')));
        if (!is_array($attestation) || !isset($attestation['authData']) || !is_string($attestation['authData'])) {
            throw new HttpError(400, 'invalid_passkey', 'Dati della passkey non validi.');
        }
        $auth = WebAuthn::authData($attestation['authData'], WebAuthn::rpIdFor($origin), true);
        list($pem, $alg) = WebAuthn::coseToPem($auth['cose']);
        $credentialId = WebAuthn::b64($auth['credentialId']);
        if ($request->str('credentialId') !== '' && $request->str('credentialId') !== $credentialId) {
            throw new HttpError(400, 'invalid_passkey', 'Identificativo della passkey diverso da quello firmato.');
        }
        if (count(self::listFor($pdo, (int)$user['id'])) >= self::MAX_PASSKEYS) {
            throw new HttpError(409, 'too_many_passkeys', 'Hai già ' . self::MAX_PASSKEYS . ' passkey: eliminane una.');
        }
        $st = $pdo->prepare('SELECT id FROM orb_passkeys WHERE credential_id = ?');
        $st->execute(array($credentialId));
        if ($st->fetch()) {
            throw new HttpError(409, 'passkey_exists', 'Questa passkey è già registrata.');
        }
        $name = Auth::cleanText($request->str('name'), 60);
        $pdo->prepare('INSERT INTO orb_passkeys (user_id, credential_id, public_key, alg, sign_count, name, created_at) VALUES (?, ?, ?, ?, ?, ?, UTC_TIMESTAMP())')
            ->execute(array($user['id'], $credentialId, $pem, $alg, $auth['signCount'], $name === '' ? 'Passkey' : $name));
        $st = $pdo->prepare('SELECT * FROM orb_passkeys WHERE credential_id = ?');
        $st->execute(array($credentialId));
        Response::json(201, array('passkey' => self::publicPasskey($st->fetch())));
    }

    public static function loginOptions(Request $request, array $params)
    {
        $pdo = Auth::pdo();
        $rpId = self::rpId($request);
        list($id, $challenge) = self::newChallenge($pdo, 'login', null);
        Response::json(200, array(
            'challengeId' => $id,
            'publicKey' => array(
                'challenge' => $challenge,
                'rpId' => $rpId,
                'timeout' => self::CHALLENGE_SECONDS * 1000,
                'userVerification' => 'required',
                'allowCredentials' => array(),
            ),
        ));
    }

    public static function loginVerify(Request $request, array $params)
    {
        $pdo = Auth::pdo();
        $device = Auth::device($request);
        $ipHash = RateLimit::ipHash($request->ip);
        RateLimit::checkLogin($pdo, $ipHash, 'passkey');
        try {
            $challenge = self::takeChallenge($pdo, $request->str('challengeId'), 'login', null);
            $credentialId = $request->str('credentialId');
            $st = $pdo->prepare('SELECT * FROM orb_passkeys WHERE credential_id = ?');
            $st->execute(array($credentialId));
            $passkey = $st->fetch();
            if (!$passkey) {
                throw new HttpError(401, 'unknown_passkey', 'Questa passkey non è collegata a nessun account.');
            }
            $clientJson = WebAuthn::unb64($request->str('clientDataJSON'));
            $origin = WebAuthn::clientData($clientJson, 'webauthn.get', $challenge);
            $authData = WebAuthn::unb64($request->str('authenticatorData'));
            $auth = WebAuthn::authData($authData, WebAuthn::rpIdFor($origin), false);
            $handle = $request->str('userHandle');
            if ($handle !== '' && !hash_equals(self::userHandle((int)$passkey['user_id']), $handle)) {
                throw new HttpError(401, 'invalid_passkey', 'La passkey non corrisponde all\'account.');
            }
            if (!WebAuthn::verifySignature($authData, $clientJson, WebAuthn::unb64($request->str('signature')), $passkey['public_key'])) {
                throw new HttpError(401, 'invalid_passkey', 'Firma della passkey non valida.');
            }
            // contatore: se l'autenticatore lo usa deve crescere (altrimenti la chiave è stata copiata)
            $stored = (int)$passkey['sign_count'];
            if ($stored > 0 && $auth['signCount'] <= $stored) {
                throw new HttpError(401, 'passkey_cloned', 'Passkey rifiutata per sicurezza: creane una nuova.');
            }
            $user = Auth::findUser($pdo, (int)$passkey['user_id']);
            if ($user === null || $user['status'] !== 'active') {
                throw new HttpError(401, 'invalid_passkey', 'Account non disponibile.');
            }
        } catch (HttpError $e) {
            RateLimit::record($pdo, $ipHash, 'passkey', 'login', false);
            throw $e;
        }
        RateLimit::record($pdo, $ipHash, 'passkey', 'login', true);
        $pdo->prepare('UPDATE orb_passkeys SET sign_count = ?, last_used_at = UTC_TIMESTAMP() WHERE id = ?')->execute(array($auth['signCount'], $passkey['id']));
        $pdo->prepare('UPDATE orb_users SET last_login_at = UTC_TIMESTAMP() WHERE id = ?')->execute(array($user['id']));
        $session = Sessions::create($pdo, (int)$user['id'], $device);
        Response::json(200, Auth::tokens($user, $session));
    }

    public static function index(Request $request, array $params)
    {
        $user = Auth::user($request);
        $out = array();
        foreach (self::listFor(Auth::pdo(), (int)$user['id']) as $row) {
            $out[] = self::publicPasskey($row);
        }
        Response::json(200, array('passkeys' => $out));
    }

    public static function delete(Request $request, array $params)
    {
        $user = Auth::user($request);
        $id = isset($params['id']) && ctype_digit($params['id']) ? (int)$params['id'] : 0;
        $st = Auth::pdo()->prepare('DELETE FROM orb_passkeys WHERE id = ? AND user_id = ?');
        $st->execute(array($id, $user['id']));
        if ($st->rowCount() === 0) {
            throw new HttpError(404, 'not_found', 'Passkey inesistente.');
        }
        Response::noContent();
    }

    // ------------------------------------------------------------------ utilità

    /** RP ID dall'origine della pagina (versione web); senza origine quella ufficiale del gioco. */
    private static function rpId(Request $request)
    {
        $origin = $request->origin;
        if (!is_string($origin) || $origin === '') {
            $origins = Config::get('cors_origins', array('https://samozzux.github.io'));
            $origin = $origins[0];
        }
        $rpId = WebAuthn::rpIdFor($origin);
        if ($rpId === null) {
            throw new HttpError(400, 'invalid_origin', 'Le passkey si usano solo dalla versione web del gioco.');
        }
        return $rpId;
    }

    /** Identificativo opaco dell'utente per l'autenticatore (non contiene dati personali). */
    private static function userHandle($userId)
    {
        return WebAuthn::b64(substr(hash_hmac('sha256', 'orbis-user:' . $userId, Auth::secret(), true), 0, 16));
    }

    private static function newChallenge(PDO $pdo, $kind, $userId)
    {
        if (mt_rand(1, 20) === 1) {
            $pdo->exec('DELETE FROM orb_passkey_challenges WHERE created_at < UTC_TIMESTAMP() - INTERVAL 1 HOUR');
        }
        $id = bin2hex(random_bytes(16));
        $challenge = WebAuthn::b64(random_bytes(32));
        $pdo->prepare('INSERT INTO orb_passkey_challenges (id, challenge, user_id, kind, created_at) VALUES (?, ?, ?, ?, UTC_TIMESTAMP())')
            ->execute(array($id, $challenge, $userId, $kind));
        return array($id, $challenge);
    }

    /** Sfida monouso: si cancella subito; deve essere dello stesso tipo, dello stesso utente e non scaduta. */
    private static function takeChallenge(PDO $pdo, $id, $kind, $userId)
    {
        if (!preg_match('/^[0-9a-f]{32}$/', (string)$id)) {
            throw new HttpError(400, 'invalid_challenge', 'Sfida della passkey scaduta: riprova.');
        }
        $st = $pdo->prepare('SELECT challenge, user_id, kind, created_at > UTC_TIMESTAMP() - INTERVAL ' . self::CHALLENGE_SECONDS . ' SECOND AS fresh
            FROM orb_passkey_challenges WHERE id = ?');
        $st->execute(array($id));
        $row = $st->fetch();
        $pdo->prepare('DELETE FROM orb_passkey_challenges WHERE id = ?')->execute(array($id));
        if (!$row || $row['kind'] !== $kind || !(int)$row['fresh'] || ($userId !== null && (int)$row['user_id'] !== $userId)) {
            throw new HttpError(400, 'invalid_challenge', 'Sfida della passkey scaduta: riprova.');
        }
        return $row['challenge'];
    }

    private static function listFor(PDO $pdo, $userId)
    {
        $st = $pdo->prepare('SELECT * FROM orb_passkeys WHERE user_id = ? ORDER BY id');
        $st->execute(array($userId));
        return $st->fetchAll();
    }

    private static function publicPasskey(array $row)
    {
        return array(
            'id' => (int)$row['id'],
            'name' => $row['name'],
            'createdAt' => Auth::iso($row['created_at']),
            'lastUsedAt' => $row['last_used_at'] !== null ? Auth::iso($row['last_used_at']) : null,
        );
    }
}
