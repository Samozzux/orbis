<?php
namespace Orbis\Api;

/** Cartella dei file del server (salvataggi, upload): storage/ protetta da .htaccess, o quella indicata nei test. */
class Storage
{
    public static function dir()
    {
        return rtrim((string)Config::get('storage_dir', ORBIS_ROOT . '/storage'), '/');
    }

    /** Percorso dentro storage/, creando le cartelle che mancano. */
    public static function path($relative)
    {
        $path = self::dir() . '/' . ltrim($relative, '/');
        $dir = dirname($path);
        if (!is_dir($dir)) {
            @mkdir($dir, 0755, true);
        }
        return $path;
    }

    public static function removeDir($dir)
    {
        if (!is_dir($dir)) {
            return;
        }
        foreach (scandir($dir) as $f) {
            if ($f !== '.' && $f !== '..') {
                @unlink($dir . '/' . $f);
            }
        }
        @rmdir($dir);
    }
}
