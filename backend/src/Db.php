<?php
namespace Orbis\Api;

use PDO;

/** Connessione PDO a MySQL (database condiviso: solo tabelle con prefisso orb_). */
class Db
{
    private static $pdo = null;
    private static $lastError = null;

    /** @return PDO|null null se la connessione non riesce (vedi lastError()) */
    public static function connection()
    {
        if (self::$pdo !== null) {
            return self::$pdo;
        }
        $db = Config::get('db', array());
        if (empty($db['name'])) {
            self::$lastError = 'not_configured';
            return null;
        }
        $host = isset($db['host']) ? $db['host'] : 'localhost';
        $port = isset($db['port']) ? (int)$db['port'] : 3306;
        $dsn = 'mysql:host=' . $host . ';port=' . $port . ';dbname=' . $db['name'];
        try {
            $pdo = new PDO($dsn, isset($db['user']) ? $db['user'] : '', isset($db['password']) ? $db['password'] : '', array(
                PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
                PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
                PDO::ATTR_EMULATE_PREPARES => false,
            ));
            $charset = self::supportsUtf8mb4($pdo) ? 'utf8mb4' : 'utf8';
            $pdo->exec('SET NAMES ' . $charset);
            $pdo->exec("SET time_zone = '+00:00'");
            self::$pdo = $pdo;
            return $pdo;
        } catch (\PDOException $e) {
            // il messaggio può contenere host/utente: si registra nel log del server, non si mostra
            error_log('[orbis] connessione DB fallita: ' . $e->getMessage());
            self::$lastError = 'connection_failed';
            return null;
        }
    }

    public static function lastError()
    {
        return self::$lastError;
    }

    public static function supportsUtf8mb4(PDO $pdo)
    {
        $row = $pdo->query("SHOW CHARACTER SET LIKE 'utf8mb4'")->fetch();
        return $row !== false;
    }

    /** Solo per i test. */
    public static function reset()
    {
        self::$pdo = null;
        self::$lastError = null;
    }
}
