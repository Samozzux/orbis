<?php
namespace Orbis\Api\Controllers;

use Orbis\Api\Auth\Auth;
use Orbis\Api\Auth\RateLimit;
use Orbis\Api\Auth\Sessions;
use Orbis\Api\Http\HttpError;
use Orbis\Api\Http\Request;
use Orbis\Api\Http\Response;

/** Registrazione, accesso, rinnovo, uscita e sessioni dei dispositivi (docs/06 §4). */
class AuthController
{
    // hash di una password a caso: anche con un username inesistente il controllo dura uguale
    const DUMMY_HASH = '$2y$10$EtJOwTBCmBKAmgD9CKf60Oy3JC3WC4sYaYLfMQTZqXc7ZC.VSLJMm';

    public static function register(Request $request, array $params)
    {
        $pdo = Auth::pdo();
        $username = $request->str('username');
        $data = $request->json();
        $password = isset($data['password']) && is_string($data['password']) ? $data['password'] : '';
        $displayName = Auth::cleanText($request->str('displayName'), 40);
        if (!preg_match('/^[A-Za-z0-9_.]{3,20}$/', $username)) {
            throw new HttpError(400, 'invalid_username', 'Il nome utente deve avere da 3 a 20 caratteri tra lettere, numeri, punto e trattino basso.');
        }
        if (strlen($password) < 8 || strlen($password) > 200) {
            throw new HttpError(400, 'invalid_password', 'La password deve avere almeno 8 caratteri.');
        }
        $device = Auth::device($request);
        $ipHash = RateLimit::ipHash($request->ip);
        RateLimit::checkRegister($pdo, $ipHash);
        if (Auth::findUserByName($pdo, $username) !== null) {
            throw new HttpError(409, 'username_taken', 'Questo nome utente è già usato.');
        }
        try {
            $pdo->prepare('INSERT INTO orb_users (username, password_hash, display_name, created_at, last_login_at) VALUES (?, ?, ?, UTC_TIMESTAMP(), UTC_TIMESTAMP())')
                ->execute(array($username, password_hash($password, PASSWORD_DEFAULT), $displayName === '' ? $username : $displayName));
        } catch (\PDOException $e) {
            // due registrazioni insieme con lo stesso nome: vince la prima
            if ($e->getCode() === '23000') {
                throw new HttpError(409, 'username_taken', 'Questo nome utente è già usato.');
            }
            throw $e;
        }
        RateLimit::record($pdo, $ipHash, $username, 'register', true);
        $user = Auth::findUser($pdo, (int)$pdo->lastInsertId());
        if ($user === null) {
            $user = Auth::findUserByName($pdo, $username);
        }
        $session = Sessions::create($pdo, (int)$user['id'], $device);
        Response::json(201, Auth::tokens($user, $session));
    }

    public static function login(Request $request, array $params)
    {
        $pdo = Auth::pdo();
        $username = $request->str('username');
        $data = $request->json();
        $password = isset($data['password']) && is_string($data['password']) ? $data['password'] : '';
        $device = Auth::device($request);
        $ipHash = RateLimit::ipHash($request->ip);
        RateLimit::checkLogin($pdo, $ipHash, $username);
        $user = strlen($username) <= 20 ? Auth::findUserByName($pdo, $username) : null;
        $ok = password_verify($password, $user !== null ? $user['password_hash'] : self::DUMMY_HASH) && $user !== null;
        if (!$ok || $user['status'] !== 'active') {
            RateLimit::record($pdo, $ipHash, $username, 'login', false);
            throw new HttpError(401, 'invalid_credentials', 'Nome utente o password non corretti.');
        }
        RateLimit::record($pdo, $ipHash, $username, 'login', true);
        if (password_needs_rehash($user['password_hash'], PASSWORD_DEFAULT)) {
            $pdo->prepare('UPDATE orb_users SET password_hash = ? WHERE id = ?')->execute(array(password_hash($password, PASSWORD_DEFAULT), $user['id']));
        }
        $pdo->prepare('UPDATE orb_users SET last_login_at = UTC_TIMESTAMP() WHERE id = ?')->execute(array($user['id']));
        $session = Sessions::create($pdo, (int)$user['id'], $device);
        Response::json(200, Auth::tokens($user, $session));
    }

    public static function refresh(Request $request, array $params)
    {
        $pdo = Auth::pdo();
        list($user, $session) = Sessions::rotate($pdo, $request->str('refreshToken'));
        Response::json(200, Auth::tokens($user, $session));
    }

    public static function logout(Request $request, array $params)
    {
        Sessions::revokeToken(Auth::pdo(), $request->str('refreshToken'));
        Response::noContent();
    }

    public static function sessions(Request $request, array $params)
    {
        $claims = null;
        $user = Auth::user($request, $claims);
        $out = array();
        foreach (Sessions::active(Auth::pdo(), (int)$user['id']) as $row) {
            $out[] = array(
                'id' => (int)$row['id'],
                'deviceName' => $row['device_name'],
                'platform' => $row['platform'],
                'lastUsedAt' => Auth::iso($row['created_at']),
                'expiresAt' => Auth::iso($row['expires_at']),
                'current' => isset($claims['did']) && $claims['did'] === $row['device_id'],
            );
        }
        Response::json(200, array('sessions' => $out));
    }

    public static function revokeSession(Request $request, array $params)
    {
        $user = Auth::user($request);
        $id = isset($params['id']) && ctype_digit($params['id']) ? (int)$params['id'] : 0;
        if (!Sessions::revokeById(Auth::pdo(), (int)$user['id'], $id)) {
            throw new HttpError(404, 'not_found', 'Sessione inesistente.');
        }
        Response::noContent();
    }
}
