-- M0: configurazione remota dell'app (docs/06 §3, orb_app_config).
-- Le altre tabelle (utenti, token, salvataggi, contenuti...) arrivano con M3.
-- Regole: solo tabelle orb_*, MySQL 5.7 compatibile, testo JSON in MEDIUMTEXT.

CREATE TABLE IF NOT EXISTS orb_app_config (
    `key` VARCHAR(100) NOT NULL,
    `value` MEDIUMTEXT NOT NULL,
    updated_at DATETIME NOT NULL,
    PRIMARY KEY (`key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT IGNORE INTO orb_app_config (`key`, `value`, updated_at) VALUES
    ('min_app_version', '0.0.0', UTC_TIMESTAMP()),
    ('world_manifest_url', '', UTC_TIMESTAMP()),
    ('message', '', UTC_TIMESTAMP());
