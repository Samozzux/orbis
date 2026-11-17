<?php
namespace Orbis\Api\Auth;

use Orbis\Api\Config;
use Orbis\Api\Db;
use Orbis\Api\Http\HttpError;
use Orbis\Api\Http\Request;
use PDO;

/** Accesso (docs/06 §4): token di accesso JWT da 1 ora e refresh token da 90 giorni per dispositivo. */
class Auth
{
    const ACCESS_TTL = 3600;
    const PLATFORMS = 'ios,android,web,editor';

    public static function secret()
    {
        $secret = (string)Config::get('jwt_secret', '');
        if ($secret === '') {
            throw new HttpError(503, 'auth_unavailable', 'Accesso non disponibile: il server non è configurato.');
        }
        return $secret;
    }

    /** @return PDO */
    public static function pdo()
    {
        $pdo = Db::connection();
        if ($pdo === null) {
            throw new HttpError(503, 'database_unavailable', 'Database non raggiungibile.');
        }
        return $pdo;
    }

    /** Utente del token di accesso (intestazione Authorization), altrimenti errore 401. */
    public static function user(Request $request, &$claims = null)
    {
        $token = $request->bearerToken();
        if ($token === '') {
            throw new HttpError(401, 'unauthorized', 'Serve l\'accesso.');
        }
        $claims = Jwt::decode($token, self::secret());
        if ($claims === null || !isset($claims['sub'])) {
            throw new HttpError(401, 'invalid_token', 'Accesso scaduto o non valido.');
        }
        $user = self::findUser(self::pdo(), (int)$claims['sub']);
        if ($user === null || $user['status'] !== 'active') {
            throw new HttpError(401, 'invalid_token', 'Account non disponibile.');
        }
        return $user;
    }

    public static function findUser(PDO $pdo, $id)
    {
        $st = $pdo->prepare('SELECT id, username, password_hash, display_name, status, is_admin, created_at FROM orb_users WHERE id = ?');
        $st->execute(array($id));
        $row = $st->fetch();
        return $row ? $row : null;
    }

    public static function findUserByName(PDO $pdo, $username)
    {
        $st = $pdo->prepare('SELECT id, username, password_hash, display_name, status, is_admin, created_at FROM orb_users WHERE username = ?');
        $st->execute(array($username));
        $row = $st->fetch();
        return $row ? $row : null;
    }

    /** Coppia di token per un dispositivo appena entrato. */
    public static function tokens(array $user, array $session)
    {
        $now = time();
        $access = Jwt::encode(array(
            'iss' => 'orbis',
            'sub' => (string)$user['id'],
            'did' => $session['device_id'],
            'iat' => $now,
            'exp' => $now + self::ACCESS_TTL,
        ), self::secret());
        return array(
            'accessToken' => $access,
            'accessExpiresIn' => self::ACCESS_TTL,
            'refreshToken' => $session['token'],
            'refreshExpiresAt' => self::iso($session['expires_at']),
            'user' => self::publicUser($user),
        );
    }

    public static function publicUser(array $user)
    {
        return array(
            'id' => (int)$user['id'],
            'username' => $user['username'],
            'displayName' => $user['display_name'],
            'createdAt' => self::iso($user['created_at']),
        );
    }

    /** DATETIME UTC del database → ISO 8601. */
    public static function iso($datetime)
    {
        if ($datetime === null || $datetime === '') {
            return null;
        }
        return gmdate('Y-m-d\TH:i:s\Z', strtotime($datetime . ' UTC'));
    }

    /** Dati del dispositivo dal corpo della richiesta (deviceId, deviceName, platform). */
    public static function device(Request $request)
    {
        $id = $request->str('deviceId');
        if (!preg_match('/^[A-Za-z0-9_-]{8,64}$/', $id)) {
            throw new HttpError(400, 'invalid_device', 'Identificativo del dispositivo non valido.');
        }
        $platform = strtolower($request->str('platform'));
        if (!in_array($platform, explode(',', self::PLATFORMS), true)) {
            throw new HttpError(400, 'invalid_device', 'Piattaforma non valida.');
        }
        $name = self::cleanText($request->str('deviceName'), 60);
        return array('id' => $id, 'name' => $name === '' ? 'Dispositivo' : $name, 'platform' => $platform);
    }

    /** Testo libero: senza caratteri di controllo e lungo al massimo $max caratteri. */
    public static function cleanText($text, $max)
    {
        $text = preg_replace('/[\x00-\x1F\x7F]/u', '', (string)$text);
        if ($text === null) {
            return '';
        }
        $text = trim($text);
        return function_exists('mb_substr') ? mb_substr($text, 0, $max, 'UTF-8') : substr($text, 0, $max);
    }
}
