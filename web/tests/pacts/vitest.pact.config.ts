import { defineConfig } from 'vitest/config';
import path from 'node:path';

/**
 * Slice OPS.1.2 — pact-specific vitest config.
 *
 * Pact spins up a real HTTP mock server on a random port to intercept
 * `apiFetch` calls; jsdom's fake XHR would collide with the mock server's
 * real sockets. Node env here + isolated `include` glob keeps this run
 * separate from the main vitest suite.
 */
export default defineConfig({
  test: {
    include: ['tests/pacts/**/*.pact.test.ts'],
    environment: 'node',
    globals: false,
    // Sequential — Pact's MockServer manages port + file writes; running
    // multiple pact interactions in parallel produces non-deterministic
    // interleave in the pact JSON.
    fileParallelism: false,
    testTimeout: 60_000,
    hookTimeout: 60_000,
    setupFiles: [],
  },
  resolve: {
    alias: {
      '@': path.resolve(__dirname, '..', '..', 'src'),
    },
  },
});
