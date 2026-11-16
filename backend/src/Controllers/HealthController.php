<?php
namespace Orbis\Api\Controllers;

use Orbis\Api\Config;
use Orbis\Api\Db;
use Orbis\Api\Http\Request;
use Orbis\Api\Http\Response;
use Orbis\Api\Migrator;

/**
 * GET /api/health (docs/06 §1): stato e capacità del server. Le scelte successive
 * (dimensione dei pezzi di upload, librerie crittografiche...) si adattano a questi valori.
 * Non espone segreti: per la configurazione dice solo se un valore è presente.
 */
class HealthController
{
    const DEFAULT_CHUNK = 524288; // 512 KB

    public static function health(Request $request, array $params)
    {
        $database = self::database();
        $ok = $database['connected'] && $database['migrations']['pending'] === 0;

        $uploadMax = self::bytes(ini_get('upload_max_filesize'));
        $postMax = self::bytes(ini_get('post_max_size'));
        $limit = min(array_filter(array($uploadMax, $postMax > 65536 ? $postMax - 65536 : $postMax), function ($v) {
            return $v > 0;
        }) ?: array(self::DEFAULT_CHUNK));

        Response::json($ok ? 200 : 503, array(
            'status' => $ok ? 'ok' : 'degraded',
            'service' => 'orbis-api',
            'apiVersion' => ORBIS_API_VERSION,
            'time' => gmdate('Y-m-d\TH:i:s\Z'),
            'https' => Request::isHttps(),
            'php' => array(
                'version' => PHP_VERSION,
                'versionId' => PHP_VERSION_ID,
            ),
            'extensions' => array(
                'openssl' => extension_loaded('openssl'),
                'zlib' => extension_loaded('zlib'),
                'mbstring' => extension_loaded('mbstring'),
                'pdo_mysql' => extension_loaded('pdo_mysql'),
                'sodium' => extension_loaded('sodium'),
                'json' => extension_loaded('json'),
                'curl' => extension_loaded('curl'),
            ),
            'functions' => array(
                'password_hash' => function_exists('password_hash'),
                'random_bytes' => function_exists('random_bytes'),
                'hash_hmac' => function_exists('hash_hmac'),
                'openssl_verify' => function_exists('openssl_verify'),
                'gzencode' => function_exists('gzencode'),
            ),
            'ini' => array(
                'upload_max_filesize' => ini_get('upload_max_filesize'),
                'post_max_size' => ini_get('post_max_size'),
                'memory_limit' => ini_get('memory_limit'),
                'max_execution_time' => (int)ini_get('max_execution_time'),
            ),
            'limits' => array(
                'chunkSizeBytes' => (int)min(self::DEFAULT_CHUNK, $limit),
            ),
            'database' => $database,
            'config' => array(
                'loaded' => Config::isLoaded(),
                'jwtSecret' => strlen((string)Config::get('jwt_secret', '')) >= 32,
                'adminUsername' => Config::get('admin_username', '') !== '',
            ),
        ));
    }

    private static function database()
    {
        $result = array(
            'connected' => false,
            'version' => null,
            'utf8mb4' => null,
            'migrations' => array('applied' => 0, 'pending' => -1, 'latest' => null),
            'error' => null,
        );
        $pdo = Db::connection();
        if ($pdo === null) {
            $result['error'] = Db::lastError();
            return $result;
        }
        $result['connected'] = true;
        try {
            $result['version'] = $pdo->query('SELECT VERSION()')->fetchColumn();
            $result['utf8mb4'] = Db::supportsUtf8mb4($pdo);
            $migrator = new Migrator($pdo, ORBIS_ROOT . '/migrations', ORBIS_ROOT . '/storage');
            $applied = $migrator->applied();
            $result['migrations'] = array(
                'applied' => count($applied),
                'pending' => count($migrator->pending()),
                'latest' => $applied ? end($applied) : null,
            );
        } catch (\Exception $e) {
            error_log('[orbis] health: ' . $e->getMessage());
            $result['error'] = 'query_failed';
        }
        return $result;
    }

    /** "2M" → 2097152 */
    private static function bytes($value)
    {
        $value = trim((string)$value);
        if ($value === '') {
            return 0;
        }
        $unit = strtolower(substr($value, -1));
        $number = (float)$value;
        switch ($unit) {
            case 'g':
                $number *= 1024;
                // continua
            case 'm':
                $number *= 1024;
                // continua
            case 'k':
                $number *= 1024;
        }
        return (int)$number;
    }
}
