CREATE TABLE users (
    id            BIGINT PRIMARY KEY,
    provider      TEXT        NOT NULL,
    provider_id   TEXT        NOT NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_login_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (provider, provider_id)
);
