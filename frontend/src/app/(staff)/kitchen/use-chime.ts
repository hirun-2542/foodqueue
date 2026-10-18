"use client";

import { useCallback, useEffect, useRef, useState, useSyncExternalStore } from "react";

/**
 * The new-order chime (story 32): a short two-tone beep made with Web Audio, so there is no sound
 * file to load.
 *
 * Browsers only let a page make sound after the user has interacted with it (autoplay policy): an
 * `AudioContext` created before that stays "suspended". So the chime is armed by the first tap or key
 * press anywhere on the screen, and until then the sound control says so instead of claiming "เปิด".
 * A page reached by in-app navigation after such a tap (e.g. the login button) already carries the
 * browser's sticky user activation, so it is armed at once without a second tap.
 * The mute choice is kept per device in localStorage, under the key of the screen that owns the
 * control (the kitchen board and the owner's new-order popup mute independently).
 */
export const KITCHEN_MUTED_KEY = "fq-kitchen-muted";

/** A burst of orders gets one chime, not one per card. */
const MIN_GAP_MS = 1_500;

const mutedListeners = new Set<() => void>();

function subscribeMuted(listener: () => void): () => void {
  mutedListeners.add(listener);
  window.addEventListener("storage", listener);
  return () => {
    mutedListeners.delete(listener);
    window.removeEventListener("storage", listener);
  };
}

function readMuted(key: string): boolean {
  return window.localStorage.getItem(key) === "1";
}

function writeMuted(key: string, muted: boolean): void {
  window.localStorage.setItem(key, muted ? "1" : "0");
  for (const listener of mutedListeners) listener();
}

export interface Chime {
  muted: boolean;
  /** The browser has let this page make sound (the user has interacted with it). */
  armed: boolean;
  setMuted: (muted: boolean) => void;
  /** Plays the beep once, unless muted, not yet armed, or another beep just played. */
  play: () => void;
}

export function useChime(mutedKey: string = KITCHEN_MUTED_KEY): Chime {
  const muted = useSyncExternalStore(subscribeMuted, () => readMuted(mutedKey), () => false);
  const [armed, setArmed] = useState(false);
  const audio = useRef<AudioContext | null>(null);
  const lastPlayed = useRef(Number.NEGATIVE_INFINITY);

  useEffect(() => {
    const arm = () => {
      audio.current ??= new AudioContext();
      const context = audio.current;
      void context.resume().then(() => {
        if (context.state !== "running") return;
        setArmed(true);
        window.removeEventListener("pointerdown", arm, true);
        window.removeEventListener("keydown", arm, true);
      });
    };
    window.addEventListener("pointerdown", arm, true);
    window.addEventListener("keydown", arm, true);
    if (navigator.userActivation?.hasBeenActive) arm();
    return () => {
      window.removeEventListener("pointerdown", arm, true);
      window.removeEventListener("keydown", arm, true);
      void audio.current?.close();
      audio.current = null;
    };
  }, []);

  const play = useCallback(() => {
    const context = audio.current;
    if (muted || !context || context.state !== "running") return;
    const now = context.currentTime;
    if (now * 1000 - lastPlayed.current < MIN_GAP_MS) return;
    lastPlayed.current = now * 1000;

    // Two 120 ms sine tones, a fifth apart, with a soft attack and release so they do not click.
    [880, 1320].forEach((frequency, index) => {
      const start = now + index * 0.14;
      const tone = context.createOscillator();
      const gain = context.createGain();
      tone.type = "sine";
      tone.frequency.value = frequency;
      gain.gain.setValueAtTime(0, start);
      gain.gain.linearRampToValueAtTime(0.25, start + 0.015);
      gain.gain.exponentialRampToValueAtTime(0.001, start + 0.12);
      tone.connect(gain).connect(context.destination);
      tone.start(start);
      tone.stop(start + 0.13);
    });
  }, [muted]);

  const setMuted = useCallback((next: boolean) => writeMuted(mutedKey, next), [mutedKey]);

  return { muted, armed, setMuted, play };
}
