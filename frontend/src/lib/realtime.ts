"use client";

import {
  type HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import {
  createContext,
  createElement,
  type ReactNode,
  use,
  useEffect,
  useEffectEvent,
  useState,
  useSyncExternalStore,
} from "react";
import type { LiveEvents } from "@/lib/types";

// Retry forever: a kitchen tablet that drops Wi-Fi for minutes must come back on its own.
const RETRY_DELAYS_MS = [0, 2_000, 5_000, 10_000];
const retryDelay = (attempt: number) => RETRY_DELAYS_MS[Math.min(attempt, RETRY_DELAYS_MS.length - 1)];

export interface LiveConnection {
  connection: HubConnection | null;
  /** True while the hub is disconnected; show the "ออฟไลน์" badge. */
  isOffline: boolean;
}

const IDLE: LiveConnection = { connection: null, isOffline: false };

/** Owns the hub connection outside React; components read it through useSyncExternalStore. */
class LiveStore {
  #connection: HubConnection | null = null;
  #offline = false;
  #retryTimer: number | undefined;
  #generation = 0;
  #snapshot: LiveConnection = IDLE;
  #listeners = new Set<() => void>();
  #onReconnect: (() => void) | null = null;

  subscribe = (listener: () => void): (() => void) => {
    this.#listeners.add(listener);
    return () => {
      this.#listeners.delete(listener);
    };
  };

  getSnapshot = (): LiveConnection => this.#snapshot;

  getServerSnapshot = (): LiveConnection => IDLE;

  setOnReconnect = (handler: (() => void) | null): void => {
    this.#onReconnect = handler;
  };

  /** Same origin, through Caddy: staff use the auth cookie, a customer passes the session token. */
  connect = (token?: string): void => {
    const generation = ++this.#generation;
    const hub = new HubConnectionBuilder()
      .withUrl(token ? `/hubs/live?token=${encodeURIComponent(token)}` : "/hubs/live")
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (ctx) => retryDelay(ctx.previousRetryCount),
      })
      .configureLogging(LogLevel.Warning)
      .build();

    this.#connection = hub;
    this.#publish();

    hub.onreconnecting(() => this.#setOffline(generation, true));
    hub.onreconnected(() => {
      this.#setOffline(generation, false);
      this.#onReconnect?.();
    });
    // withAutomaticReconnect only covers drops after a successful start, so the first start retries itself.
    hub.onclose(() => void this.#start(generation, hub, 1));

    void this.#start(generation, hub, 0);
  };

  dispose = (): void => {
    this.#generation += 1;
    window.clearTimeout(this.#retryTimer);
    this.#retryTimer = undefined;
    const hub = this.#connection;
    this.#connection = null;
    this.#offline = false;
    this.#publish();
    if (hub && hub.state !== HubConnectionState.Disconnected) void hub.stop();
  };

  #start = async (generation: number, hub: HubConnection, attempt: number): Promise<void> => {
    try {
      await hub.start();
      if (generation !== this.#generation) return;
      if (attempt > 0) {
        this.#setOffline(generation, false);
        this.#onReconnect?.();
      }
    } catch {
      if (generation !== this.#generation) return;
      this.#setOffline(generation, true);
      this.#retryTimer = window.setTimeout(
        () => void this.#start(generation, hub, attempt + 1),
        retryDelay(attempt + 1),
      );
    }
  };

  #setOffline(generation: number, offline: boolean): void {
    if (generation !== this.#generation || this.#offline === offline) return;
    this.#offline = offline;
    this.#publish();
  }

  #publish(): void {
    this.#snapshot = { connection: this.#connection, isOffline: this.#offline };
    for (const listener of this.#listeners) listener();
  }
}

/**
 * The one place a SignalR connection to `/hubs/live` is built. After any reconnect every TanStack
 * query is invalidated, because events sent while offline are lost.
 */
export function useLiveConnection({ token }: { token?: string } = {}): LiveConnection {
  const queryClient = useQueryClient();
  const [store] = useState(() => new LiveStore());

  useEffect(() => {
    store.setOnReconnect(() => {
      void queryClient.invalidateQueries();
    });
    return () => store.setOnReconnect(null);
  }, [store, queryClient]);

  useEffect(() => {
    store.connect(token);
    return () => store.dispose();
  }, [store, token]);

  return useSyncExternalStore(store.subscribe, store.getSnapshot, store.getServerSnapshot);
}

const LiveContext = createContext<LiveConnection | null>(null);

/** Opens the live connection for everything rendered inside it. Mount once per screen tree. */
export function LiveProvider({ token, children }: { token?: string; children: ReactNode }) {
  const live = useLiveConnection({ token });
  return createElement(LiveContext, { value: live }, children);
}

function useLiveContext(): LiveConnection {
  const live = use(LiveContext);
  if (!live) throw new Error("useLiveEvent/useLiveOffline must be used inside <LiveProvider>");
  return live;
}

/** Subscribes to one hub event for the lifetime of the component; `handler` may change freely. */
export function useLiveEvent<K extends keyof LiveEvents>(
  name: K,
  handler: (payload: LiveEvents[K]) => void,
): void {
  const { connection } = useLiveContext();
  const onEvent = useEffectEvent(handler);

  useEffect(() => {
    if (!connection) return;
    const listener = (payload: LiveEvents[K]) => onEvent(payload);
    connection.on(name, listener);
    return () => connection.off(name, listener);
  }, [connection, name]);
}

/** Whether the surrounding live connection is currently down. */
export function useLiveOffline(): boolean {
  return useLiveContext().isOffline;
}
