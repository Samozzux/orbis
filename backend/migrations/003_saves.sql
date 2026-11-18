-- M3: salvataggi nel cloud con revisioni e upload a pezzi (docs/06 §3 e §5).
-- Il contenuto dei salvataggi sta in storage/saves/ (cartella protetta), qui solo i metadati.

CREATE TABLE IF NOT EXISTS orb_saves (
    id INT UNSIGNED NOT NULL AUTO_INCREMENT,
    user_id INT UNSIGNED NOT NULL,
    save_uuid CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    name VARCHAR(60) NOT NULL,
    city_name VARCHAR(60) NOT NULL,
    revision INT UNSIGNED NOT NULL,
    device_id VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    device_name VARCHAR(60) NOT NULL,
    size_bytes INT UNSIGNED NOT NULL,
    checksum CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    storage_path VARCHAR(255) NOT NULL,
    meta MEDIUMTEXT NOT NULL,
    updated_at DATETIME NOT NULL,
    is_shared_snapshot TINYINT(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    UNIQUE KEY uq_orb_saves_user_uuid (user_id, save_uuid)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- upload in corso: i pezzi stanno in storage/uploads/<upload_id>/ finché non si chiude l'upload
CREATE TABLE IF NOT EXISTS orb_save_uploads (
    upload_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    user_id INT UNSIGNED NOT NULL,
    save_uuid CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    base_revision INT UNSIGNED NOT NULL,
    size_bytes INT UNSIGNED NOT NULL,
    chunk_count INT UNSIGNED NOT NULL,
    checksum CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    name VARCHAR(60) NOT NULL,
    city_name VARCHAR(60) NOT NULL,
    device_id VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    device_name VARCHAR(60) NOT NULL,
    meta MEDIUMTEXT NOT NULL,
    created_at DATETIME NOT NULL,
    PRIMARY KEY (upload_id),
    KEY ix_orb_save_uploads_user (user_id, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS orb_save_chunks (
    upload_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    chunk_index INT UNSIGNED NOT NULL,
    size INT UNSIGNED NOT NULL,
    checksum CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at DATETIME NOT NULL,
    PRIMARY KEY (upload_id, chunk_index)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
