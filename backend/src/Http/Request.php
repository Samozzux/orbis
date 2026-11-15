<?php
namespace Orbis\Api\Http;

class Request
{
    public $method;
    public $path;
    public $origin;
    public $query;

    public static function fromGlobals()
    {
        $r = new Request();
        $r->method = isset($_SERVER['REQUEST_METHOD']) ? strtoupper($_SERVER['REQUEST_METHOD']) : 'GET';
        $r->path = self::routePath();
        $r->origin = isset($_SERVER['HTTP_ORIGIN']) ? $_SERVER['HTTP_ORIGIN'] : '';
        $r->query = $_GET;
        return $r;
    }

    /**
     * Percorso della rotta, es. "/health". Funziona con:
     * /orbis/api/health (rewrite), /orbis/api/index.php/health (PATH_INFO), /orbis/api/?route=health.
     */
    private static function routePath()
    {
        if (isset($_GET['route']) && is_string($_GET['route'])) {
            return '/' . trim($_GET['route'], '/');
        }
        if (!empty($_SERVER['PATH_INFO'])) {
            return '/' . trim($_SERVER['PATH_INFO'], '/');
        }
        $uri = isset($_SERVER['REQUEST_URI']) ? parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH) : '/';
        if (!is_string($uri)) {
            $uri = '/';
        }
        $script = isset($_SERVER['SCRIPT_NAME']) ? $_SERVER['SCRIPT_NAME'] : '';
        $base = rtrim(str_replace('\\', '/', dirname($script)), '/');
        if ($base !== '' && strpos($uri, $base) === 0) {
            $uri = substr($uri, strlen($base));
        }
        $uri = preg_replace('#^/index\.php#', '', $uri);
        return '/' . trim($uri, '/');
    }

    public static function isHttps()
    {
        if (!empty($_SERVER['HTTPS']) && strtolower($_SERVER['HTTPS']) !== 'off') {
            return true;
        }
        if (isset($_SERVER['HTTP_X_FORWARDED_PROTO']) && strtolower($_SERVER['HTTP_X_FORWARDED_PROTO']) === 'https') {
            return true;
        }
        if (isset($_SERVER['HTTP_X_FORWARDED_SSL']) && strtolower($_SERVER['HTTP_X_FORWARDED_SSL']) === 'on') {
            return true;
        }
        return isset($_SERVER['SERVER_PORT']) && (int)$_SERVER['SERVER_PORT'] === 443;
    }
}
