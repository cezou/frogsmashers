package store

import (
	"crypto/rand"
	"encoding/binary"
)

// maxGeneratedID caps generated ids below 2^52. Valid SteamID64
// individual-account values start at 0x0110000100000000 (~7.66e16),
// far above this range, so a generated id can never collide with a
// future Steam user whose id IS their steamid64. Staying below 2^53
// also keeps ids exact in float64-based JSON parsers.
const maxGeneratedID = int64(1) << 52

// steamID64Base is the lowest valid individual SteamID64.
const steamID64Base = int64(0x0110000100000000)

// newUserID returns a uniform random id in [1, 2^52) for users whose
// identity provider has no numeric 64-bit id of its own (e.g. UGS).
func newUserID() (int64, error) {
	var buf [8]byte
	for {
		if _, err := rand.Read(buf[:]); err != nil {
			return 0, err
		}
		id := int64(binary.BigEndian.Uint64(buf[:])) & (maxGeneratedID - 1)
		if id != 0 {
			return id, nil
		}
	}
}
