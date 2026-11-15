<?php
namespace Orbis\Api;

use PDO;

/**
 * Migrazioni automatiche (docs/06 §2): alla prima richiesta dopo un deploy applica i file
 * migrations/NNN_nome.sql mancanti, registrandoli in orb_migrations. Un lock MySQL evita
 * esecuzioni doppie; un file in storage/ evita di interrogare il DB a ogni richiesta.
 */
class Migrator
{
    const LOCK_NAME = 'orb_migrate';

    private $pdo;
    private $dir;
    private $markerFile;

    public function __construct(PDO $pdo, $migrationsDir, $storageDir)
    {
        $this->pdo = $pdo;
        $this->dir = rtrim($migrationsDir, '/');
        $this->markerFile = rtrim($storageDir, '/') . '/migrations.ok';
    }

    /** @return string[] nomi dei file .sql in ordine */
    public function files()
    {
        $files = glob($this->dir . '/*.sql');
        if ($files === false) {
            return array();
        }
        $names = array_map('basename', $files);
        sort($names, SORT_STRING);
        return $names;
    }

    public function applied()
    {
        $this->ensureTable();
        $rows = $this->pdo->query('SELECT name FROM orb_migrations ORDER BY name')->fetchAll(PDO::FETCH_COLUMN);
        return $rows ? $rows : array();
    }

    public function pending()
    {
        return array_values(array_diff($this->files(), $this->applied()));
    }

    /** @return int migrazioni applicate in questa chiamata */
    public function migrate($force = false)
    {
        $fingerprint = sha1(implode('|', $this->files()));
        if (!$force && is_file($this->markerFile) && trim((string)@file_get_contents($this->markerFile)) === $fingerprint) {
            return 0;
        }
        $got = (int)$this->pdo->query("SELECT GET_LOCK('" . self::LOCK_NAME . "', 15)")->fetchColumn();
        if ($got !== 1) {
            return 0; // un'altra richiesta sta già migrando
        }
        $count = 0;
        try {
            foreach ($this->pending() as $name) {
                $this->apply($name);
                $count++;
            }
            @file_put_contents($this->markerFile, $fingerprint);
        } finally {
            $this->pdo->query("SELECT RELEASE_LOCK('" . self::LOCK_NAME . "')");
        }
        return $count;
    }

    private function ensureTable()
    {
        $this->pdo->exec($this->adapt(
            'CREATE TABLE IF NOT EXISTS orb_migrations (
                id INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
                name VARCHAR(190) NOT NULL,
                applied_at DATETIME NOT NULL,
                UNIQUE KEY uq_orb_migrations_name (name)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci'
        ));
    }

    private function apply($name)
    {
        $sql = file_get_contents($this->dir . '/' . $name);
        foreach (self::splitStatements($sql) as $statement) {
            if (!preg_match('/\borb_[a-z0-9_]+/i', $statement)) {
                throw new \RuntimeException("Migrazione $name: ogni istruzione deve riguardare tabelle orb_*");
            }
            $this->pdo->exec($this->adapt($statement));
        }
        $stmt = $this->pdo->prepare('INSERT INTO orb_migrations (name, applied_at) VALUES (?, UTC_TIMESTAMP())');
        $stmt->execute(array($name));
    }

    /** Divide un file SQL in istruzioni (separatore: ";" a fine riga), ignorando i commenti "--". */
    public static function splitStatements($sql)
    {
        $lines = preg_split('/\r?\n/', $sql);
        $clean = array();
        foreach ($lines as $line) {
            if (preg_match('/^\s*--/', $line)) {
                continue;
            }
            $clean[] = $line;
        }
        $parts = preg_split('/;\s*(\n|$)/', implode("\n", $clean));
        $out = array();
        foreach ($parts as $p) {
            $p = trim($p);
            if ($p !== '') {
                $out[] = $p;
            }
        }
        return $out;
    }

    /** Se il server non ha utf8mb4 (MySQL molto vecchio) si ripiega su utf8. */
    private function adapt($sql)
    {
        if (Db::supportsUtf8mb4($this->pdo)) {
            return $sql;
        }
        return str_replace(array('utf8mb4_unicode_ci', 'utf8mb4'), array('utf8_unicode_ci', 'utf8'), $sql);
    }
}
