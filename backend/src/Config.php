<?php
namespace Orbis\Api;

/**
 * Configurazione: config.php generato dal deploy (mai nel repo).
 * Per i test si può indicare un altro file con la variabile d'ambiente ORBIS_CONFIG_FILE.
 */
class Config
{
    private static $values = null;

    public static function all()
    {
        if (self::$values === null) {
            $file = getenv('ORBIS_CONFIG_FILE');
            if (!$file) {
                $file = ORBIS_ROOT . '/config.php';
            }
            $loaded = is_file($file) ? require $file : array();
            self::$values = is_array($loaded) ? $loaded : array();
        }
        return self::$values;
    }

    public static function get($key, $default = null)
    {
        $all = self::all();
        return array_key_exists($key, $all) ? $all[$key] : $default;
    }

    public static function isLoaded()
    {
        return count(self::all()) > 0;
    }
}
