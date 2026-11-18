<?php
namespace Orbis\Api\Http;

use Orbis\Api\Config;

/**
 * CORS (docs/06 §4): ammessa l'origine della versione web (https://samozzux.github.io)
 * e, per lo sviluppo, localhost / 127.0.0.1 su qualsiasi porta.
 */
class Cors
{
    public static function isAllowed($origin)
    {
        if (!is_string($origin) || $origin === '') {
            return false;
        }
        $allowed = Config::get('cors_origins', array('https://samozzux.github.io'));
        if (in_array($origin, $allowed, true)) {
            return true;
        }
        if (Config::get('cors_allow_localhost', true)) {
            return preg_match('#^https?://(localhost|127\.0\.0\.1|\[::1\])(:\d{1,5})?$#', $origin) === 1;
        }
        return false;
    }

    /** Aggiunge gli header CORS se l'origine è ammessa. */
    public static function apply($origin)
    {
        header('Vary: Origin');
        if (!self::isAllowed($origin)) {
            return false;
        }
        header('Access-Control-Allow-Origin: ' . $origin);
        header('Access-Control-Allow-Methods: GET, POST, PUT, PATCH, DELETE, OPTIONS');
        header('Access-Control-Allow-Headers: Authorization, Content-Type, Range');
        header('Access-Control-Expose-Headers: ETag, Content-Range, Retry-After, X-Orbis-Revision');
        header('Access-Control-Max-Age: 86400');
        return true;
    }
}
