-- M3: account, sessioni dei dispositivi e limite ai tentativi di accesso (docs/06 §3-4).
-- Regole: solo tabelle orb_*, MySQL 5.7 compatibile.

CREATE TABLE IF NOT EXISTS orb_users (
    id INT UNSIGNED NOT NULL AUTO_INCREMENT,
    username VARCHAR(20) NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    display_name VARCHAR(40) NOT NULL,
    is_admin TINYINT(1) NOT NULL DEFAULT 0,
    status VARCHAR(16) NOT NULL DEFAULT 'active',
    created_at DATETIME NOT NULL,
    last_login_at DATETIME NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_orb_users_username (username)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- refresh token: solo l'hash; ogni rinnovo crea un token nuovo e revoca il vecchio (replaced_by)
CREATE TABLE IF NOT EXISTS orb_refresh_tokens (
    id INT UNSIGNED NOT NULL AUTO_INCREMENT,
    user_id INT UNSIGNED NOT NULL,
    token_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    device_id VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    device_name VARCHAR(60) NOT NULL,
    platform VARCHAR(10) NOT NULL,
    created_at DATETIME NOT NULL,
    expires_at DATETIME NOT NULL,
    revoked_at DATETIME NULL,
    replaced_by INT UNSIGNED NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_orb_refresh_tokens_hash (token_hash),
    KEY ix_orb_refresh_tokens_device (user_id, device_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- tentativi di accesso e registrazioni (l'IP è salvato solo come HMAC)
CREATE TABLE IF NOT EXISTS orb_login_attempts (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    ip_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    username VARCHAR(20) NOT NULL,
    kind VARCHAR(10) NOT NULL DEFAULT 'login',
    success TINYINT(1) NOT NULL,
    created_at DATETIME NOT NULL,
    PRIMARY KEY (id),
    KEY ix_orb_login_attempts_ip (ip_hash, created_at),
    KEY ix_orb_login_attempts_user (username, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
