package db

import (
	"context"
	"embed"
	"fmt"
	"sort"

	"github.com/jackc/pgx/v5/pgxpool"
)

//go:embed migrations/*.sql
var migrationFS embed.FS

// Migrate applies every embedded migration that is not yet recorded
// in schema_migrations, each inside its own transaction.
func Migrate(ctx context.Context, pool *pgxpool.Pool) error {
	_, err := pool.Exec(ctx, `
		CREATE TABLE IF NOT EXISTS schema_migrations (
			version    TEXT PRIMARY KEY,
			applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
		)`)
	if err != nil {
		return fmt.Errorf("create schema_migrations: %w", err)
	}

	entries, err := migrationFS.ReadDir("migrations")
	if err != nil {
		return err
	}
	names := make([]string, 0, len(entries))
	for _, e := range entries {
		names = append(names, e.Name())
	}
	sort.Strings(names)

	for _, name := range names {
		if err := applyOnce(ctx, pool, name); err != nil {
			return fmt.Errorf("migration %s: %w", name, err)
		}
	}
	return nil
}

func applyOnce(ctx context.Context, pool *pgxpool.Pool, name string) error {
	var applied bool
	err := pool.QueryRow(ctx, `
		SELECT EXISTS (
			SELECT 1 FROM schema_migrations WHERE version = $1
		)`, name).Scan(&applied)
	if err != nil {
		return err
	}
	if applied {
		return nil
	}

	sql, err := migrationFS.ReadFile("migrations/" + name)
	if err != nil {
		return err
	}

	tx, err := pool.Begin(ctx)
	if err != nil {
		return err
	}
	defer tx.Rollback(ctx)

	if _, err := tx.Exec(ctx, string(sql)); err != nil {
		return err
	}
	_, err = tx.Exec(ctx,
		`INSERT INTO schema_migrations (version) VALUES ($1)`, name)
	if err != nil {
		return err
	}
	return tx.Commit(ctx)
}
