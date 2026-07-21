// Package store persists users keyed by a 64-bit id compatible with
// a future steamid64, plus (provider, provider_id) identity columns.
package store

import (
	"context"
	"errors"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgconn"
	"github.com/jackc/pgx/v5/pgxpool"
)

var (
	// ErrExists means the (provider, provider_id) pair is already
	// registered.
	ErrExists = errors.New("user already exists")
	// ErrNotFound means no user matches (provider, provider_id).
	ErrNotFound = errors.New("user not found")
)

type User struct {
	ID          int64
	Provider    string
	ProviderID  string
	CreatedAt   time.Time
	LastLoginAt time.Time
}

type UserStore interface {
	// Create registers a new user, returning ErrExists when the
	// (provider, providerID) pair is taken.
	Create(ctx context.Context, provider, providerID string) (User, error)
	// Login touches last_login_at and returns the user, or
	// ErrNotFound when the pair is not registered.
	Login(ctx context.Context, provider, providerID string) (User, error)
}

const userColumns = "id, provider, provider_id, created_at, last_login_at"

type PGUserStore struct {
	pool *pgxpool.Pool
}

func NewPGUserStore(pool *pgxpool.Pool) *PGUserStore {
	return &PGUserStore{pool: pool}
}

func (s *PGUserStore) Create(
	ctx context.Context, provider, providerID string,
) (User, error) {
	const maxAttempts = 3
	for range maxAttempts {
		id, err := newUserID()
		if err != nil {
			return User{}, err
		}
		row := s.pool.QueryRow(ctx, `
			INSERT INTO users (id, provider, provider_id)
			VALUES ($1, $2, $3)
			RETURNING `+userColumns, id, provider, providerID)
		u, err := scanUser(row)
		if err == nil {
			return u, nil
		}
		var pgErr *pgconn.PgError
		if errors.As(err, &pgErr) && pgErr.Code == "23505" {
			if pgErr.ConstraintName == "users_pkey" {
				continue
			}
			return User{}, ErrExists
		}
		return User{}, err
	}
	return User{}, errors.New("user id collision retries exhausted")
}

func (s *PGUserStore) Login(
	ctx context.Context, provider, providerID string,
) (User, error) {
	row := s.pool.QueryRow(ctx, `
		UPDATE users SET last_login_at = now()
		WHERE provider = $1 AND provider_id = $2
		RETURNING `+userColumns, provider, providerID)
	u, err := scanUser(row)
	if errors.Is(err, pgx.ErrNoRows) {
		return User{}, ErrNotFound
	}
	return u, err
}

func scanUser(row pgx.Row) (User, error) {
	var u User
	err := row.Scan(&u.ID, &u.Provider, &u.ProviderID,
		&u.CreatedAt, &u.LastLoginAt)
	return u, err
}
