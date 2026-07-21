// Package config loads the API configuration from environment
// variables. On the VM the required ones come from
// /etc/frogsmashers/db.env and /etc/frogsmashers/api.env via systemd.
package config

import (
	"fmt"
	"os"
	"strings"
	"time"
)

const (
	defaultListenAddr = "127.0.0.1:8080"
	defaultUGSIssuer  = "https://player-auth.services.api.unity.com"
	defaultJWTTTL     = 24 * time.Hour
)

type Config struct {
	DatabaseURL  string
	JWTSecret    []byte
	JWTTTL       time.Duration
	ListenAddr   string
	UGSProjectID string
	UGSIssuer    string
	UGSJWKSURL   string
}

// Load reads the configuration, reporting every missing required
// variable in a single error.
func Load() (Config, error) {
	cfg := Config{
		DatabaseURL:  os.Getenv("DATABASE_URL"),
		JWTSecret:    []byte(os.Getenv("JWT_SECRET")),
		JWTTTL:       defaultJWTTTL,
		ListenAddr:   envOr("LISTEN_ADDR", defaultListenAddr),
		UGSProjectID: os.Getenv("UGS_PROJECT_ID"),
		UGSIssuer:    envOr("UGS_ISSUER", defaultUGSIssuer),
	}
	cfg.UGSJWKSURL = envOr("UGS_JWKS_URL",
		cfg.UGSIssuer+"/.well-known/jwks.json")

	if ttl := os.Getenv("JWT_TTL"); ttl != "" {
		d, err := time.ParseDuration(ttl)
		if err != nil {
			return Config{}, fmt.Errorf("invalid JWT_TTL %q: %w", ttl, err)
		}
		cfg.JWTTTL = d
	}

	var missing []string
	if cfg.DatabaseURL == "" {
		missing = append(missing, "DATABASE_URL")
	}
	if len(cfg.JWTSecret) == 0 {
		missing = append(missing, "JWT_SECRET")
	}
	if cfg.UGSProjectID == "" {
		missing = append(missing, "UGS_PROJECT_ID")
	}
	if len(missing) > 0 {
		return Config{}, fmt.Errorf("missing required env vars: %s",
			strings.Join(missing, ", "))
	}
	return cfg, nil
}

func envOr(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}
