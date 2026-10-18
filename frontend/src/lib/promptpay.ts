import generatePayload from "promptpay-qr";

/**
 * Ticket 19 — the EMVCo PromptPay payload, built on the client from the shop's configured ID and the
 * bill's total (spec "Implementation Decisions": `PromptPay QR สร้างฝั่ง frontend จาก PromptPay ID กับยอดรวม`).
 *
 * `promptpay-qr@0.5.0` is the plan's encoder and it does run in the browser under this Next 16 build
 * (its CommonJS `crc`→`buffer` chain is aliased to Next's own `next/dist/compiled/buffer` polyfill),
 * so no hand-written encoder is needed here: this file only fixes the inputs, the emptiness rule and
 * the display form.
 *
 * Payload shape, for the record (`promptpay-qr` produces exactly this, verified by decoding the
 * rendered QR — see `frontend/e2e/promptpay-qr.spec.ts`):
 * `00` payload format `01`, `01` POI `12` (dynamic, because an amount is present),
 * `29` merchant account template — `00` AID `A000000677010111` + `01` target (a 10-digit phone becomes
 * `0066…`), `53` currency `764`, `58` country `TH`, `54` amount with two decimals, `63` CRC-16/CCITT-FALSE
 * (poly `0x1021`, init `0xFFFF`, no reflection, no final XOR) over everything before it.
 *
 * Both exported functions are pure: the total and the ID are the only inputs.
 */

/**
 * The payload to render as the payment QR, or `null` when there is nothing honest to encode.
 *
 * Returns `null` rather than a placeholder when no ID is configured (the screen then keeps the option
 * disabled) and when the total is not a positive amount — `promptpay-qr` would silently drop the amount
 * and emit a *static* QR, which is not the "QR ตามยอดบิล" this screen promises. An Open bill always has
 * a positive total, so on this screen a `null` payload means the shop has not set an ID.
 */
export function promptPayPayload(promptPayId: string | null | undefined, total: number): string | null {
  const digits = (promptPayId ?? "").replace(/\D/g, "");
  if (digits.length === 0) return null;
  if (!Number.isFinite(total) || total <= 0) return null;
  return generatePayload(digits, { amount: total });
}

/**
 * The ID as frame `o05` writes it in the QR caption: `081-234-5678`. Punctuation the owner typed is
 * folded away and a 10-digit mobile is re-grouped; a citizen/tax ID or e-wallet account is long enough
 * to read as itself, so it stays as configured.
 */
export function formatPromptPayId(promptPayId: string): string {
  const trimmed = promptPayId.trim();
  const digits = trimmed.replace(/\D/g, "");
  return digits.length === 10 && digits.startsWith("0")
    ? `${digits.slice(0, 3)}-${digits.slice(3, 6)}-${digits.slice(6)}`
    : trimmed;
}
