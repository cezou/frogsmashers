// The FrogSmashers accounts API: verifies an identity-provider
// credential (anonymous UGS today, Steam later) and issues the
// home-grown JWT used as Bearer by every other endpoint.
package main

import (
	"context"
	"log/slog"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/cezou/frogsmashers/api/internal/auth"
	"github.com/cezou/frogsmashers/api/internal/config"
	"github.com/cezou/frogsmashers/api/internal/db"
	"github.com/cezou/frogsmashers/api/internal/httpapi"
	"github.com/cezou/frogsmashers/api/internal/identity"
	"github.com/cezou/frogsmashers/api/internal/store"
)

func main() {
	log := slog.New(slog.NewTextHandler(os.Stderr, nil))
	if err := run(log); err != nil {
		log.Error("fatal", "err", err)
		os.Exit(1)
	}
}

func run(log *slog.Logger) error {
	ctx, stop := signal.NotifyContext(context.Background(),
		syscall.SIGINT, syscall.SIGTERM)
	defer stop()

	cfg, err := config.Load()
	if err != nil {
		return err
	}

	pool, err := db.Connect(ctx, cfg.DatabaseURL)
	if err != nil {
		return err
	}
	defer pool.Close()

	if err := db.Migrate(ctx, pool); err != nil {
		return err
	}

	ugs, err := identity.NewUGSProvider(ctx,
		cfg.UGSJWKSURL, cfg.UGSIssuer, cfg.UGSProjectID)
	if err != nil {
		return err
	}

	server := httpapi.NewServer(
		store.NewPGUserStore(pool),
		identity.Registry{"ugs": ugs},
		auth.NewIssuer(cfg.JWTSecret, cfg.JWTTTL),
		pool,
		log,
	)

	httpServer := &http.Server{
		Addr:              cfg.ListenAddr,
		Handler:           httpapi.NewRouter(server),
		ReadHeaderTimeout: 5 * time.Second,
	}

	errc := make(chan error, 1)
	go func() { errc <- httpServer.ListenAndServe() }()
	log.Info("listening", "addr", cfg.ListenAddr)

	select {
	case err := <-errc:
		return err
	case <-ctx.Done():
		shutdownCtx, cancel := context.WithTimeout(
			context.Background(), 5*time.Second)
		defer cancel()
		return httpServer.Shutdown(shutdownCtx)
	}
}
