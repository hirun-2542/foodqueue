"use client";

import { CheckIcon, MergeIcon, PinIcon, XIcon } from "lucide-react";
import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogTitle,
} from "@/components/ui/dialog";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/lib/api";
import { formatBaht, formatTime } from "@/lib/format";
import { useMergePreview, useMergeSessions } from "@/lib/sessions";
import type { MergePreviewTableDto, OrderDto } from "@/lib/types";

/**
 * รวมบิล (ticket 22) — the `รวมบิล` button of the queue card's secondary action row (frame o08) and the
 * dialog it opens (frame o19). The round is the card's `sessionId`; the round of a table always merges as
 * a whole, so any card of it will do.
 */
export function MergeBillAction({ order }: { order: OrderDto }) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <Button
        variant="ghost"
        className="h-11 flex-1 gap-2 px-2 text-[15px] text-ink-2"
        aria-haspopup="dialog"
        aria-label={`รวมบิลของโต๊ะ ${order.tableName}`}
        onClick={() => setOpen(true)}
      >
        <MergeIcon className="size-[18px]" strokeWidth={1.75} aria-hidden />
        รวมบิล
      </Button>
      {open ? <MergeBillDialog sessionId={order.sessionId} onClose={() => setOpen(false)} /> : null}
    </>
  );
}

/**
 * Frame o19: the primary round, every other round that can still join it (all of them pre-selected, as
 * the frame shows), and the merged bill underneath — subtotal of every chosen round, then service charge
 * and VAT applied once, exactly as `BillCalculator` does on the server. The preview comes from the server
 * (`POST /api/sessions/{id}/merge/preview`) so the numbers shown here are the numbers that will exist;
 * confirming runs every rule again, and merging is not reversible.
 */
function MergeBillDialog({ sessionId, onClose }: { sessionId: number; onClose: () => void }) {
  // Merging cannot be undone, so nothing is ticked for the owner: every tick is their own decision.
  const [selected, setSelected] = useState<number[]>([]);
  const merge = useMergeSessions(sessionId);
  const preview = useMergePreview(sessionId, selected);
  const data = preview.data;
  const picked = selected;

  const toggle = (id: number) =>
    setSelected(picked.includes(id) ? picked.filter((one) => one !== id) : [...picked, id]);

  const submit = () => {
    if (picked.length === 0 || merge.isPending) return;
    merge.mutate({ sessionIds: picked }, { onSuccess: onClose });
  };

  const error = merge.error instanceof ApiError ? merge.error : null;
  const pickedNames = [data?.primaryTableName, ...rowsOf(data?.mergeable, picked).map((row) => row.tableName)]
    .filter(Boolean)
    .join(" + ");

  return (
    <Dialog open onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent
        showCloseButton={false}
        className="max-h-[calc(100dvh-2rem)] gap-0 overflow-y-auto p-0 sm:max-w-[720px]"
      >
        <div className="flex items-start gap-3 px-6 pt-5 pb-4">
          <div className="flex grow flex-col gap-1">
            <DialogTitle className="text-[24px] leading-[1.35] font-semibold tracking-[-0.01em]">
              รวมบิล
            </DialogTitle>
            <DialogDescription className="text-[13px] leading-[1.55]">
              รวมโต๊ะที่ยังไม่ปิดบิลเข้าบิลเดียว · ลูกค้าแต่ละโต๊ะยังสั่งผ่าน QR เดิมได้
            </DialogDescription>
          </div>
          <DialogClose asChild>
            <Button variant="ghost" size="icon" aria-label="ปิด">
              <XIcon className="size-6" strokeWidth={1.75} aria-hidden />
            </Button>
          </DialogClose>
        </div>
        <hr className="border-border" />

        {preview.isPending ? (
          <div className="grid gap-5 px-6 py-5 sm:grid-cols-[1fr_300px]">
            <Skeleton className="h-64 rounded-xl" />
            <Skeleton className="h-64 rounded-xl" />
          </div>
        ) : !data ? (
          <div className="px-6 py-5">
            <p className="rounded-xl bg-sunken p-4 text-body text-ink-2">
              {preview.error instanceof ApiError
                ? preview.error.message
                : "โหลดข้อมูลรวมบิลไม่ได้ ลองอีกครั้ง"}
            </p>
          </div>
        ) : (
          <div className="grid items-start gap-5 px-6 py-5 sm:grid-cols-[1fr_300px]">
            <div className="flex min-w-0 flex-col">
              <span className="text-label font-semibold whitespace-nowrap text-ink-2">โต๊ะหลัก</span>
              <div className="mt-2 flex flex-wrap items-center gap-x-2.5 gap-y-1 rounded-xl bg-sunken px-4 py-3">
                <PinIcon className="size-4 flex-none text-muted-foreground" strokeWidth={1.75} aria-hidden />
                <b className="font-mono text-headline font-medium whitespace-nowrap">{data.primaryTableName}</b>
                <Badge variant="accent" className="before:hidden whitespace-nowrap">
                  {data.primaryPackageName}
                </Badge>
                <span className="text-sm whitespace-nowrap text-ink-2">
                  {data.primaryAdults} ผู้ใหญ่
                  {data.primaryChildren > 0 ? ` ${data.primaryChildren} เด็ก` : ""} · เปิด{" "}
                  <span className="font-mono">{formatTime(data.primaryOpenedAt)}</span>
                </span>
                <b className="ml-auto flex-none text-title tabular-nums">{formatBaht(data.primaryTotal)}</b>
              </div>

              <div className="mt-5 mb-1.5 flex items-center justify-between gap-3">
                <span className="text-label font-semibold whitespace-nowrap text-ink-2">
                  รวมโต๊ะอื่นเข้าบิลนี้
                </span>
                <span className="text-[12.5px] text-muted-foreground">
                  เลือกแล้ว <b className="tabular-nums">{picked.length}</b> โต๊ะ
                </span>
              </div>

              {data.candidates.length === 0 && data.mergeable.length === 0 ? (
                <p className="mt-2 rounded-xl bg-sunken px-4 py-3 text-sm text-ink-2">
                  ตอนนี้ไม่มีโต๊ะอื่นที่รวมได้ · รวมได้ทุกโต๊ะที่ยังไม่ปิดบิล
                </p>
              ) : null}

              <div role="group" aria-label="โต๊ะที่จะรวมเข้าบิลนี้" className="flex flex-col">
                {[...data.mergeable, ...data.candidates].map((row) => (
                  <MergeRow
                    key={row.sessionId}
                    row={row}
                    checked={picked.includes(row.sessionId)}
                    onToggle={() => toggle(row.sessionId)}
                  />
                ))}
              </div>

              <p className="mt-2.5 text-[12.5px] text-muted-foreground">
                รวมได้ทุกโต๊ะที่ยังไม่ปิดบิล แม้ครัวยังทำรอบล่าสุดไม่เสร็จ
              </p>
            </div>

            <div className="flex min-w-0 flex-col">
              <div className="flex flex-col rounded-xl bg-sunken p-4">
                <div className="mb-1.5 flex items-center justify-between gap-3">
                  <span className="text-sm font-semibold">บิลรวม</span>
                  <Badge variant="accent" className="before:hidden">
                    <span className="tabular-nums">{picked.length + 1}</span> โต๊ะ
                  </Badge>
                </div>

                <PreviewRow label={`ยอดโต๊ะ ${data.primaryTableName}`} amount={data.primarySubtotal} />
                {rowsOf(data.mergeable, picked).map((row) => (
                  <PreviewRow key={row.sessionId} label={`ยอดโต๊ะ ${row.tableName}`} amount={row.subtotal} />
                ))}

                <hr className="my-1.5 border-border" />
                <PreviewRow label="ยอดก่อนค่าบริการ" amount={data.bill.subtotal} />
                <PreviewRow
                  label={`ค่าบริการ ${data.bill.serviceChargePercent}%`}
                  amount={data.bill.serviceCharge}
                />
                <PreviewRow label={`VAT ${data.bill.vatPercent}%`} amount={data.bill.vat} />

                <div className="mt-2 flex items-center justify-between gap-3 border-t border-border pt-2.5">
                  <span className="text-title">รวมทั้งสิ้น</span>
                  <span className="text-[28px] leading-[1.15] font-semibold tabular-nums">
                    {formatBaht(data.bill.total)}
                  </span>
                </div>
              </div>

              <p className="mt-3 text-[12.5px] leading-[1.55] text-muted-foreground">
                ลูกค้าของทุกโต๊ะสั่งผ่าน QR เดิมได้ตามปกติ · ชำระครั้งเดียวที่บิลของ{" "}
                <span className="font-mono">{data.primaryTableName}</span> · ใบเสร็จออกใบเดียว
              </p>
            </div>
          </div>
        )}

        <hr className="border-border" />
        <div className="flex flex-col gap-3 px-6 py-4">
          {error ? (
            <p className="rounded-lg bg-cancel-soft px-3 py-2 text-label text-cancel-ink" role="alert">
              {error.message}
            </p>
          ) : null}
          <div className="flex items-center justify-end gap-2">
            <Button variant="ghost" className="h-12 px-5" onClick={onClose} disabled={merge.isPending}>
              ยกเลิก
            </Button>
            <Button
              className="h-12 px-5"
              disabled={!data || picked.length === 0 || merge.isPending}
              onClick={submit}
            >
              <MergeIcon className="size-4" strokeWidth={1.75} aria-hidden />
              {merge.isPending ? "กำลังรวม…" : `รวมบิล ${pickedNames}`}
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** One selectable table of the list: check, table code, package and head count, its own bill total. */
function MergeRow({
  row,
  checked,
  onToggle,
}: {
  row: MergePreviewTableDto;
  checked: boolean;
  onToggle: () => void;
}) {
  return (
    <button
      type="button"
      role="checkbox"
      aria-checked={checked}
      aria-label={`โต๊ะ ${row.tableName} · ${row.packageName}`}
      onClick={onToggle}
      className="flex h-12 items-center gap-2.5 border-t border-border text-left outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
    >
      <span
        className={`flex size-[22px] flex-none items-center justify-center rounded-md border ${
          checked ? "border-orange bg-orange text-on-accent" : "border-control-border bg-surface"
        }`}
        aria-hidden
      >
        {checked ? <CheckIcon className="size-4" strokeWidth={2.5} /> : null}
      </span>
      <b className="min-w-8 font-mono text-[18px] font-semibold whitespace-nowrap">{row.tableName}</b>
      <span className="grow truncate text-sm text-muted-foreground">
        {row.packageName} · <span className="tabular-nums">{row.adults + row.children}</span> คน
      </span>
      <b className="tabular-nums">{formatBaht(row.total)}</b>
    </button>
  );
}

function PreviewRow({ label, amount }: { label: string; amount: number }) {
  return (
    <div className="flex items-center justify-between gap-3 py-1">
      <span className="text-ink-2">{label}</span>
      <span className="tabular-nums">{formatBaht(amount)}</span>
    </div>
  );
}

/** The picked rows in the order the server listed them (the list the panel draws). */
function rowsOf(rows: MergePreviewTableDto[] | undefined, picked: number[]): MergePreviewTableDto[] {
  return (rows ?? []).filter((row) => picked.includes(row.sessionId));
}
