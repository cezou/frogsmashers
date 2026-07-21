package store

import "testing"

func TestNewUserIDRange(t *testing.T) {
	for range 10000 {
		id, err := newUserID()
		if err != nil {
			t.Fatalf("newUserID: %v", err)
		}
		if id < 1 || id >= maxGeneratedID {
			t.Fatalf("id %d outside [1, 2^52)", id)
		}
	}
}

func TestGeneratedIDsNeverCollideWithSteamIDs(t *testing.T) {
	if maxGeneratedID >= steamID64Base {
		t.Fatalf("generated id range [1, %d) overlaps steamid64 "+
			"range starting at %d", maxGeneratedID, steamID64Base)
	}
}
