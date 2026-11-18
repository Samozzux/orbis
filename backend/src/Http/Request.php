<?php
namespace Orbis\Api\Http;

class Request
{
    const MAX_JSON_BYTES = 65536;

    public $method;
    public $path;
    public $origin;
    public $query;
    /** @var array intestazioni con nome in minuscolo */
    public $headers = array();
    public $ip = '';
    private $rawBody = null;
    private $json = null;

    public static function fromGlobals()
    {
        $r = new Request();
        $r->method = isset($_SERVER['REQUEST_METHOD']) ? strtoupper($_SERVER['REQUEST_METHOD']) : 'GET';
        $r->path = self::routePath();
        $r->origin = isset($_SERVER['HTTP_ORIGIN']) ? $_SERVER['HTTP_ORIGIN'] : '';
        $r->query = $_GET;
        $r->headers = self::collectHeaders();
        $r->ip = isset($_SERVER['REMOTE_ADDR']) ? (string)$_SERVER['REMOTE_ADDR'] : '';
        return $r;
    }

    public function header($name)
    {
        $name = strtolower($name);
        return isset($this->headers[$name]) ? $this->headers[$name] : '';
    }

    /** Token dell'intestazione "Authorization: Bearer ..." (stringa vuota se manca). */
    public function bearerToken()
    {
        $auth = $this->header('authorization');
        if (preg_match('#^Bearer\s+(\S+)$#i', trim($auth), $m)) {
            return $m[1];
        }
        return '';
    }

    public function rawBody()
    {
        if ($this->rawBody === null) {
            $body = file_get_contents('php://input');
            $this->rawBody = $body === false ? '' : $body;
        }
        return $this->rawBody;
    }

    /** Corpo JSON come array; errore 400 se non è un oggetto JSON valido. */
    public function json()
    {
        if ($this->json !== null) {
            return $this->json;
        }
        $raw = $this->rawBody();
        if (strlen($raw) > self::MAX_JSON_BYTES) {
            throw new HttpError(413, 'payload_too_large', 'Richiesta troppo grande.');
        }
        $data = $raw === '' ? array() : json_decode($raw, true);
        if (!is_array($data)) {
            throw new HttpError(400, 'invalid_json', 'Il corpo della richiesta non è un JSON valido.');
        }
        $this->json = $data;
        return $data;
    }

    /** Campo stringa del corpo JSON (tagliato), o $default se manca. */
    public function str($key, $default = '')
    {
        $data = $this->json();
        if (!isset($data[$key])) {
            return $default;
        }
        return is_scalar($data[$key]) ? trim((string)$data[$key]) : $default;
    }

    /** Solo per i test. */
    public static function make($method, $path, array $headers = array(), $body = '')
    {
        $r = new Request();
        $r->method = $method;
        $r->path = $path;
        $r->origin = '';
        $r->query = array();
        foreach ($headers as $k => $v) {
            $r->headers[strtolower($k)] = $v;
        }
        $r->rawBody = $body;
        return $r;
    }

    private static function collectHeaders()
    {
        $out = array();
        foreach ($_SERVER as $k => $v) {
            if (strpos($k, 'HTTP_') === 0) {
                $out[strtolower(str_replace('_', '-', substr($k, 5)))] = (string)$v;
            }
        }
        // con PHP in CGI/FPM l'intestazione Authorization arriva solo tramite il rewrite di .htaccess
        if (!isset($out['authorization']) && !empty($_SERVER['REDIRECT_HTTP_AUTHORIZATION'])) {
            $out['authorization'] = (string)$_SERVER['REDIRECT_HTTP_AUTHORIZATION'];
        }
        if (!isset($out['authorization']) && function_exists('getallheaders')) {
            foreach (getallheaders() as $k => $v) {
                if (strtolower($k) === 'authorization') {
                    $out['authorization'] = (string)$v;
                }
            }
        }
        if (isset($_SERVER['CONTENT_TYPE'])) {
            $out['content-type'] = (string)$_SERVER['CONTENT_TYPE'];
        }
        if (isset($_SERVER['CONTENT_LENGTH'])) {
            $out['content-length'] = (string)$_SERVER['CONTENT_LENGTH'];
        }
        return $out;
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
