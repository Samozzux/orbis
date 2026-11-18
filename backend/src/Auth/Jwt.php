<?php
namespace Orbis\Api\Auth;

/** JWT HS256 minimale (docs/06 §4): token di accesso firmati con il segreto del server. */
class Jwt
{
    public static function encode(array $claims, $secret)
    {
        $header = self::b64(json_encode(array('alg' => 'HS256', 'typ' => 'JWT')));
        $payload = self::b64(json_encode($claims));
        $signature = self::b64(hash_hmac('sha256', $header . '.' . $payload, $secret, true));
        return $header . '.' . $payload . '.' . $signature;
    }

    /** Claims del token se la firma è valida e non è scaduto, altrimenti null. */
    public static function decode($token, $secret, $now = null)
    {
        if (!is_string($token) || $secret === '' || substr_count($token, '.') !== 2) {
            return null;
        }
        list($h, $p, $s) = explode('.', $token);
        $header = json_decode(self::unb64($h), true);
        if (!is_array($header) || !isset($header['alg']) || $header['alg'] !== 'HS256') {
            return null;
        }
        $expected = self::b64(hash_hmac('sha256', $h . '.' . $p, $secret, true));
        if (!hash_equals($expected, $s)) {
            return null;
        }
        $claims = json_decode(self::unb64($p), true);
        if (!is_array($claims) || !isset($claims['exp']) || !is_numeric($claims['exp'])) {
            return null;
        }
        $now = $now === null ? time() : $now;
        if ((int)$claims['exp'] <= $now) {
            return null;
        }
        return $claims;
    }

    public static function b64($data)
    {
        return rtrim(strtr(base64_encode($data), '+/', '-_'), '=');
    }

    public static function unb64($data)
    {
        $decoded = base64_decode(strtr($data, '-_', '+/'), true);
        return $decoded === false ? '' : $decoded;
    }
}
