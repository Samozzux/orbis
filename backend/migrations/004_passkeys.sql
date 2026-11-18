-- M3: passkey per la versione web (WebAuthn, docs/06 §4). Solo tabelle orb_*, MySQL 5.7 compatibile.

-- credenziali registrate: id della credenziale (base64url), chiave pubblica PEM, algoritmo COSE, contatore
CREATE TABLE IF NOT EXISTS orb_passkeys (
    id INT UNSIGNED NOT NULL AUTO_INCREMENT,
    user_id INT UNSIGNED NOT NULL,
    credential_id VARCHAR(255) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    public_key TEXT NOT NULL,
    alg INT NOT NULL,
    sign_count INT UNSIGNED NOT NULL DEFAULT 0,
    name VARCHAR(60) NOT NULL,
    created_at DATETIME NOT NULL,
    last_used_at DATETIME NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_orb_passkeys_credential (credential_id),
    KEY ix_orb_passkeys_user (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- sfide monouso (5 minuti) per creare una passkey o entrare con una passkey
CREATE TABLE IF NOT EXISTS orb_passkey_challenges (
    id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    challenge CHAR(43) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    user_id INT UNSIGNED NULL,
    kind VARCHAR(10) NOT NULL,
    created_at DATETIME NOT NULL,
    PRIMARY KEY (id),
    KEY ix_orb_passkey_challenges_created (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
