import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { queryClient } from "@/lib/query/queryClient";
import {
  FakeEventSource,
  fakeEventSourceInstances,
  latestFakeEventSource,
} from "@/test/fakeEventSource";
import { shareApi } from "../api/shareApi";
import { shareKeys } from "./useShare";
import { useEventShareStream } from "./useEventShareStream";

/**
 * `useEventShareStream` — pure hook test over the public-share live-update
 * stream (`public-share-sse-updates.md`). `FakeEventSource` is already the
 * default global `EventSource` installed by `src/test/setup.ts`, so no extra
 * `vi.stubGlobal` is needed here; this spec only drives the double's
 * registry/`dispatch(...)` helper. Spies on the singleton `queryClient` the
 * hook imports directly (not via `useQueryClient()` context) to assert the
 * exact `invalidateQueries` calls without a `QueryClientProvider` wrapper.
 * No real network, no timers — every "server frame" is a synchronous
 * `dispatch(...)` call.
 */

describe("useEventShareStream", () => {
  let invalidateSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");
  });

  afterEach(() => {
    invalidateSpy.mockRestore();
  });

  describe("connection lifecycle", () => {
    it("UseEventShareStream_Disabled_NeverConstructsEventSource", () => {
      renderHook(() => useEventShareStream("tok-1", { enabled: false }));
      expect(fakeEventSourceInstances()).toHaveLength(0);
    });

    it("UseEventShareStream_EmptyToken_NeverConstructsEventSourceEvenWhenEnabled", () => {
      renderHook(() => useEventShareStream("", { enabled: true }));
      expect(fakeEventSourceInstances()).toHaveLength(0);
    });

    it("UseEventShareStream_EnabledWithToken_ConstructsExactlyOneEventSourceAtStreamUrl", () => {
      renderHook(() => useEventShareStream("tok-1", { enabled: true }));
      expect(fakeEventSourceInstances()).toHaveLength(1);
      expect(latestFakeEventSource()?.url).toBe(
        shareApi.publicStreamUrl("tok-1"),
      );
    });

    it("UseEventShareStream_RerenderSameProps_DoesNotOpenSecondConnection", () => {
      const { rerender } = renderHook(
        ({ token, enabled }: { token: string; enabled: boolean }) =>
          useEventShareStream(token, { enabled }),
        { initialProps: { token: "tok-1", enabled: true } },
      );
      expect(fakeEventSourceInstances()).toHaveLength(1);

      rerender({ token: "tok-1", enabled: true });

      expect(fakeEventSourceInstances()).toHaveLength(1);
    });

    it("UseEventShareStream_Unmount_ClosesConnectionEvenWithNoTerminalEvent", () => {
      const { unmount } = renderHook(() =>
        useEventShareStream("tok-1", { enabled: true }),
      );
      const source = latestFakeEventSource();
      expect(source?.readyState).toBe(FakeEventSource.OPEN);

      unmount();

      expect(source?.readyState).toBe(FakeEventSource.CLOSED);
    });

    it("UseEventShareStream_TokenChangesWhileMounted_ClosesOldOpensNewAndResetsTerminalReason", () => {
      const { result, rerender } = renderHook(
        ({ token, enabled }: { token: string; enabled: boolean }) =>
          useEventShareStream(token, { enabled }),
        { initialProps: { token: "tok-1", enabled: true } },
      );
      const first = latestFakeEventSource();
      act(() => {
        first?.dispatch("revoked");
      });
      expect(result.current.terminalReason).toBe("revoked");
      expect(first?.readyState).toBe(FakeEventSource.CLOSED);

      rerender({ token: "tok-2", enabled: true });

      // Old connection stays closed; exactly one new connection opened at the
      // new token's URL, and the stale terminal reason is cleared.
      expect(first?.readyState).toBe(FakeEventSource.CLOSED);
      expect(fakeEventSourceInstances()).toHaveLength(2);
      const second = latestFakeEventSource();
      expect(second).not.toBe(first);
      expect(second?.url).toBe(shareApi.publicStreamUrl("tok-2"));
      expect(result.current.terminalReason).toBeNull();
    });
  });

  describe("event handling", () => {
    it("UseEventShareStream_ConnectedEvent_IsNoOp", () => {
      const { result } = renderHook(() =>
        useEventShareStream("tok-1", { enabled: true }),
      );

      latestFakeEventSource()?.dispatch("connected");

      expect(result.current.terminalReason).toBeNull();
      expect(invalidateSpy).not.toHaveBeenCalled();
    });

    it("UseEventShareStream_UpdatedEvent_InvalidatesReportAndQrListCaches", () => {
      renderHook(() => useEventShareStream("tok-1", { enabled: true }));

      latestFakeEventSource()?.dispatch("updated");

      expect(invalidateSpy).toHaveBeenCalledWith({
        queryKey: shareKeys.public("tok-1"),
      });
      expect(invalidateSpy).toHaveBeenCalledWith({
        queryKey: shareKeys.publicQrs("tok-1"),
      });
      expect(invalidateSpy).toHaveBeenCalledTimes(2);
    });

    it("UseEventShareStream_RevokedEvent_SetsTerminalReasonAndClosesConnection", () => {
      const { result } = renderHook(() =>
        useEventShareStream("tok-1", { enabled: true }),
      );
      const source = latestFakeEventSource();

      act(() => {
        source?.dispatch("revoked");
      });

      expect(result.current.terminalReason).toBe("revoked");
      expect(source?.readyState).toBe(FakeEventSource.CLOSED);
    });

    it("UseEventShareStream_ExpiredEvent_SetsTerminalReasonAndClosesConnection", () => {
      const { result } = renderHook(() =>
        useEventShareStream("tok-1", { enabled: true }),
      );
      const source = latestFakeEventSource();

      act(() => {
        source?.dispatch("expired");
      });

      expect(result.current.terminalReason).toBe("expired");
      expect(source?.readyState).toBe(FakeEventSource.CLOSED);
    });
  });
});
