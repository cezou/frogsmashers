package store_test

import (
	"context"
	"errors"
	"os"
	"testing"

	"github.com/cezou/frogsmashers/api/internal/db"
	"github.com/cezou/frogsmashers/api/internal/store"
)

// TestPGUserStore exercises the real migrations and store against
// PostgreSQL. Gated on TEST_DATABASE_URL, e.g.:
//
//	docker run --rm -d -p 5433:5432 -e POSTGRES_PASSWORD=test \
//	    --name frog-test-pg postgres:17
//	TEST_DATABASE_URL=postgres://postgres:test@localhost:5433/postgres \
//	    go test ./...
func TestPGUserStore(t *testing.T) {
	url := os.Getenv("TEST_DATABASE_URL")
	if url == "" {
		t.Skip("TEST_DATABASE_URL not set")
	}
	ctx := context.Background()

	pool, err := db.Connect(ctx, url)
	if err != nil {
		t.Fatalf("connect: %v", err)
	}
	defer pool.Close()

	_, err = pool.Exec(ctx,
		`DROP TABLE IF EXISTS users, schema_migrations`)
	if err != nil {
		t.Fatalf("reset: %v", err)
	}
	if err := db.Migrate(ctx, pool); err != nil {
		t.Fatalf("migrate: %v", err)
	}
	if err := db.Migrate(ctx, pool); err != nil {
		t.Fatalf("migrate must be idempotent: %v", err)
	}

	s := store.NewPGUserStore(pool)

	created, err := s.Create(ctx, "ugs", "player-1")
	if err != nil {
		t.Fatalf("create: %v", err)
	}
	if created.ID <= 0 {
		t.Fatalf("bad id %d", created.ID)
	}

	if _, err := s.Create(ctx, "ugs", "player-1"); !errors.Is(
		err, store.ErrExists) {
		t.Fatalf("duplicate create: err = %v, want ErrExists", err)
	}

	logged, err := s.Login(ctx, "ugs", "player-1")
	if err != nil {
		t.Fatalf("login: %v", err)
	}
	if logged.ID != created.ID {
		t.Fatalf("login id %d != created id %d", logged.ID, created.ID)
	}
	if logged.LastLoginAt.Before(created.LastLoginAt) {
		t.Fatal("last_login_at not touched")
	}

	if _, err := s.Login(ctx, "ugs", "nobody"); !errors.Is(
		err, store.ErrNotFound) {
		t.Fatalf("unknown login: err = %v, want ErrNotFound", err)
	}

	if _, err := s.Create(ctx, "steam", "player-1"); err != nil {
		t.Fatalf("same provider_id under other provider: %v", err)
	}
}
