/**
 * Minimal `EventSource` test double for the public-share live-stream feature
 * (`useEventShareStream`). Neither MSW (fetch/XHR-only, no `EventSource`
 * interception layer) nor jsdom (`EventSource` is on its documented
 * "not implemented" list) can stand in for the real transport, so tests
 * install this via `vi.stubGlobal("EventSource", FakeEventSource)` instead
 * (planning doc `public-share-sse-updates.md`, OQ3).
 *
 * Implements only the surface the hook actually uses: named
 * `addEventListener`/`removeEventListener`, `close()`, `readyState`, and the
 * `CONNECTING`/`OPEN`/`CLOSED` constants (both as static members, matching
 * the real `EventSource`, and as instance members for convenience). Every
 * instance ever constructed is tracked in a module-level registry so a test
 * can grab the one the hook created and call its test-only `dispatch(...)`
 * to simulate a named server frame without any real network.
 */

type FakeEventSourceListener = (event: MessageEvent) => void;

const READY_STATE = { CONNECTING: 0, OPEN: 1, CLOSED: 2 } as const;

export class FakeEventSource {
  static readonly CONNECTING = READY_STATE.CONNECTING;
  static readonly OPEN = READY_STATE.OPEN;
  static readonly CLOSED = READY_STATE.CLOSED;

  readonly CONNECTING = READY_STATE.CONNECTING;
  readonly OPEN = READY_STATE.OPEN;
  readonly CLOSED = READY_STATE.CLOSED;

  readonly url: string;
  readyState: number;

  private readonly listenersByType = new Map<string, Set<FakeEventSourceListener>>();

  constructor(url: string) {
    this.url = url;
    // Real EventSource starts CONNECTING then flips OPEN once the connection
    // succeeds; tests never need to observe the CONNECTING window, so this
    // double goes straight to OPEN — simplest thing that satisfies the hook,
    // which never reads `readyState` itself (only `close()` matters to it).
    this.readyState = READY_STATE.OPEN;
    instances.push(this);
  }

  addEventListener(type: string, listener: FakeEventSourceListener): void {
    let set = this.listenersByType.get(type);
    if (!set) {
      set = new Set();
      this.listenersByType.set(type, set);
    }
    set.add(listener);
  }

  removeEventListener(type: string, listener: FakeEventSourceListener): void {
    this.listenersByType.get(type)?.delete(listener);
  }

  close(): void {
    this.readyState = READY_STATE.CLOSED;
  }

  /**
   * Test-only: simulate a named server frame (`connected` / `updated` /
   * `revoked` / `expired`) by constructing a `MessageEvent` and invoking every
   * listener registered for that event name — mirrors what a real
   * `EventSource` does internally when a `data:`/`event:` frame arrives.
   */
  dispatch(eventName: string, data = "{}"): void {
    const event = new MessageEvent(eventName, { data });
    for (const listener of this.listenersByType.get(eventName) ?? []) {
      listener(event);
    }
  }
}

let instances: FakeEventSource[] = [];

/** All `FakeEventSource` instances constructed since the last `resetFakeEventSources()`. */
export function fakeEventSourceInstances(): FakeEventSource[] {
  return instances;
}

/** The most recently constructed instance, or `undefined` if none yet exist. */
export function latestFakeEventSource(): FakeEventSource | undefined {
  return instances[instances.length - 1];
}

/**
 * Clear the registry. Call in a test's `afterEach` alongside
 * `vi.unstubAllGlobals()` so instances from one test never leak into the next.
 */
export function resetFakeEventSources(): void {
  instances = [];
}
