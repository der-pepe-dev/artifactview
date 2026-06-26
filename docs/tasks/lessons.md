# Lessons

Correction patterns and recurring mistakes. Append a dated entry after any user
correction or hard-won lesson. Newest first.

<!-- - YYYY-MM-DD: <what went wrong> -> <the rule to follow next time> -->
- 2026-06-26: Running the Infrastructure test suite over `/mnt/w` (drvfs) repeatedly wedged
  the Windows-drive mount with EIO (and a SIGABRT host crash under TUnit parallelism) ->
  run the test suite from an **ext4 copy** (e.g. `rsync` to `~/av-run`), edit canonical
  source on `/mnt/w`. Don't trust a test crash on drvfs as a code bug until reproduced on ext4.
- 2026-06-26: `perl -i` on drvfs deletes the file (rename EPERM) -> use `sed -i` / Edit, never `perl -i` on `/mnt/w`.
- 2026-06-26: The xUnit→TUnit code fixer (TUXU0001) silently mis-converts several idioms ->
  always re-run tests after using it. Known traps: `Assert.Equal(arr,arr)` (by-value) →
  `.IsEqualTo` (by-REFERENCE) needs `.IsEquivalentTo`; `Assert.That(byteVal).IsEqualTo(0xFF /*int*/)`
  throws InvalidOperationException, cast expected `(byte)`; dropped `StringComparer`/`ignoreCase`
  args (use `.IgnoringCase()`); `Assert.All`/`Assert.Single(pred)`/predicate-`Contains` arg-swaps.
