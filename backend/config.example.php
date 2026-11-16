<?php
// Modello di configurazione. Il vero config.php NON è nel repo: lo genera il workflow
// .github/workflows/backend.yml dai GitHub Secrets. Per provare in locale copialo in config.php.
return array(
    'env' => 'development',
    'db' => array(
        'host' => 'localhost',
        'name' => 'my_giuliaa8',
        'user' => 'giuliaa8',
        'password' => '',
        'port' => 3306,
    ),
    // chiave di firma dei token (openssl rand -hex 32), usata da M3
    'jwt_secret' => '',
    // username dell'account admin del gioco
    'admin_username' => '',
    // origini ammesse per CORS (oltre a localhost se cors_allow_localhost è true)
    'cors_origins' => array('https://samozzux.github.io'),
    'cors_allow_localhost' => true,
);
