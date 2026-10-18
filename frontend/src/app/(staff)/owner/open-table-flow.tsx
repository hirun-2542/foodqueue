"use client";

import { QRCodeSVG } from "qrcode.react";
import { useQuery } from "@tanstack/react-query";
import { CheckIcon, LinkIcon, MinusIcon, PlusIcon, PrinterIcon, QrCodeIcon } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError, apiFetch } from "@/lib/api";
import { formatBaht, formatBahtWhole, formatTime } from "@/lib/format";
import { useOpenSession, type OpenSessionInput } from "@/lib/sessions";
import type { PackageDto, SessionDetailDto } from "@/lib/types";

/**
 * The open-table flow of frames `o02-open` and `o03-qr`: choose a package and the guest counts, then
 * show the QR of the round that was just created.
 *
 * Both dialogs are self-contained so every screen that offers เปิดโต๊ะ mounts the same pair — the
 * overview grid (frame `o01`) and the floor map's side panel (frame `o12`) cannot drift apart:
 * neither screen owns a copy of "which table is taken"; that comes from `GET /api/tables/overview`.
 *
 * Package tier pricing and the buffet arithmetic read the same way they did when ticket 07 built
 * this; the round's start/end preview is a snapshot taken when the dialog opens (never a clock read
 * during render) and the authoritative times come from the server's answer.
 */
export function OpenTableDialog({
  tableId,
  tableName,
  previewStart,
  onCancel,
  onDone,
}: {
  tableId: number;
  tableName: string;
  /** Clock snapshot taken when the owner opened this dialog; pure to render, never a countdown. */
  previewStart: number;
  onCancel: () => void;
  /** Called with the round the server created; the caller closes this dialog and shows the QR. */
  onDone: (session: SessionDetailDto) => void;
}) {
  return (
    <OpenTableForm
      key={tableId}
      tableId={tableId}
      tableName={tableName}
      previewStart={previewStart}
      onCancel={onCancel}
      onDone={onDone}
    />
  );
}

function OpenTableForm({
  tableId,
  tableName,
  previewStart,
  onCancel,
  onDone,
}: {
  tableId: number;
  tableName: string;
  previewStart: number;
  onCancel: () => void;
  onDone: (session: SessionDetailDto) => void;
}) {
  const packages = useQuery({
    queryKey: ["packages"],
    queryFn: () => apiFetch<PackageDto[]>("/api/packages"),
  });
  const openSession = useOpenSession();

  const [packageId, setPackageId] = useState<number | null>(null);
  const [adults, setAdults] = useState(2);
  const [children, setChildren] = useState(1);

  const activePackages = (packages.data ?? []).filter((pkg) => pkg.isActive);
  const chosen = activePackages.find((pkg) => pkg.id === packageId) ?? activePackages[0] ?? null;
  const buffetTotal = chosen ? adults * chosen.adultPrice + children * chosen.childPrice : 0;

  const submit = async () => {
    if (!chosen) return;
    const input: OpenSessionInput = {
      tableId,
      packageId: chosen.id,
      adultCount: adults,
      childCount: children,
    };
    try {
      onDone(await openSession.mutateAsync(input));
    } catch {
      // The message shows next to the button that caused it.
    }
  };

  return (
    <Dialog open onOpenChange={(next) => (next ? undefined : onCancel())}>
      <DialogContent className="sm:max-w-[560px]">
        <DialogHeader>
          <DialogTitle className="text-headline">เปิดโต๊ะ {tableName}</DialogTitle>
          <DialogDescription>
            เลือกแพ็กเกจและจำนวนผู้ใหญ่/เด็ก ระบบจะสร้าง QR สำหรับรอบการนั่งนี้
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-2">
          <div className="flex items-center justify-between gap-3">
            <span className="text-label text-ink-2">แพ็กเกจ</span>
            <span className="text-xs text-muted-foreground">แพ็กเกจอันดับสูงสั่งเมนูได้มากกว่า</span>
          </div>

          {packages.isPending ? (
            <Skeleton className="h-16 w-full rounded-lg" />
          ) : activePackages.length === 0 ? (
            <p className="rounded-lg border border-border bg-sunken p-3 text-body text-muted-foreground">
              ยังไม่มีแพ็กเกจที่ใช้งานอยู่ — เปิดใช้งานแพ็กเกจในหน้า “แพ็กเกจ” ก่อนเปิดโต๊ะ
            </p>
          ) : (
            <ul className="flex flex-col gap-2">
              {activePackages.map((pkg) => {
                const on = pkg.id === chosen?.id;
                return (
                  <li key={pkg.id}>
                    <button
                      type="button"
                      aria-pressed={on}
                      onClick={() => setPackageId(pkg.id)}
                      className={`flex w-full items-center gap-3 rounded-lg border px-3.5 py-3 text-left ${
                        on
                          ? "border-orange-line bg-orange-soft"
                          : "border-border bg-surface hover:bg-sunken"
                      }`}
                    >
                      <span className="flex flex-1 flex-col gap-1">
                        <span className="flex items-center gap-2">
                          <b className={`text-title ${on ? "text-orange-ink" : ""}`}>{pkg.name}</b>
                          <span className="text-xs text-muted-foreground">อันดับ {pkg.rank}</span>
                        </span>
                        <span className="text-label font-normal text-muted-foreground">
                          ผู้ใหญ่ {formatBahtWhole(pkg.adultPrice)} · เด็ก {formatBahtWhole(pkg.childPrice)} ·{" "}
                          {pkg.durationMinutes} นาที
                        </span>
                      </span>
                      <span
                        className={`flex size-6 flex-none items-center justify-center rounded-md border ${
                          on ? "border-orange-line bg-primary text-primary-foreground" : "border-control-border"
                        }`}
                      >
                        {on ? <CheckIcon className="size-4" aria-hidden /> : null}
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
        </div>

        <div className="flex flex-col gap-3.5">
          <CountStepper
            id="adults"
            label="ผู้ใหญ่"
            hint="ต้องมีอย่างน้อย 1 ท่าน"
            value={adults}
            min={1}
            onChange={setAdults}
          />
          <CountStepper
            id="children"
            label="เด็ก"
            hint="ไม่บังคับ"
            value={children}
            min={0}
            onChange={setChildren}
          />
        </div>

        <div className="rounded-lg bg-sunken p-4">
          <div className="flex items-center justify-between gap-4">
            <span className="text-label">
              ค่าบุฟเฟ่ต์ {adults} × {formatBahtWhole(chosen?.adultPrice ?? 0)} + {children} ×{" "}
              {formatBahtWhole(chosen?.childPrice ?? 0)}
            </span>
            <b className="text-title tabular-nums">{formatBaht(buffetTotal)}</b>
          </div>
          <p className="mt-1.5 text-label text-muted-foreground">
            เปิด <span className="font-mono">{formatTime(previewStart)}</span> · หมดเวลา{" "}
            <span className="font-mono">
              {formatTime(previewStart + (chosen?.durationMinutes ?? 0) * 60_000)}
            </span>{" "}
            · ยังไม่รวมค่าบริการและ VAT
          </p>
        </div>

        {openSession.error ? (
          <p role="alert" className="text-body text-cancel-ink">
            {openSession.error instanceof ApiError
              ? openSession.error.message
              : "เปิดโต๊ะไม่สำเร็จ ลองอีกครั้ง"}
          </p>
        ) : null}

        <DialogFooter className="border-t-0 bg-transparent">
          <Button type="button" variant="ghost" onClick={onCancel}>
            ยกเลิก
          </Button>
          <Button
            type="button"
            size="lg"
            disabled={openSession.isPending || chosen === null}
            onClick={() => void submit()}
          >
            <QrCodeIcon aria-hidden />
            เปิดโต๊ะและสร้าง QR
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/**
 * Frame `o03-qr`. The QR is the real `qrUrl` from the server (`{App:PublicBaseUrl}/s/{token}`), so a
 * phone on the same network opens the round it just created. The QR-slip print page is ticket 20's
 * route, linked here and not faked.
 */
export function QrResultDialog({ session, onClose }: { session: SessionDetailDto; onClose: () => void }) {
  const durationMinutes = Math.round((Date.parse(session.endsAt) - Date.parse(session.openedAt)) / 60_000);

  return (
    <Dialog open onOpenChange={(next) => (next ? undefined : onClose())}>
      <DialogContent className="sm:max-w-[560px]">
        <DialogHeader>
          <DialogTitle className="text-headline">โต๊ะ {session.tableName} เปิดแล้ว</DialogTitle>
          <DialogDescription>พิมพ์ slip ไปวางที่โต๊ะ หรือให้ลูกค้าสแกนจากจอนี้ได้เลย</DialogDescription>
        </DialogHeader>

        <p className="flex items-center gap-2 rounded-lg bg-ready-soft px-3 py-2.5 text-label text-ready-ink">
          <CheckIcon className="size-4 flex-none" aria-hidden />
          สร้าง QR สำหรับรอบการนั่งนี้แล้ว · ใช้ได้จนกว่าจะปิดบิล
        </p>

        <div className="mx-auto flex size-[244px] items-center justify-center rounded-xl border border-border bg-surface">
          <QRCodeSVG value={session.qrUrl} size={222} level="M" />
        </div>

        <div className="flex items-center justify-center gap-2">
          <span className="max-w-[240px] truncate font-mono text-label text-muted-foreground">
            {session.qrUrl.replace(/^https?:\/\/[^/]+/, "…")}
          </span>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() =>
              void navigator.clipboard
                .writeText(session.qrUrl)
                .then(() => toast.success("คัดลอกลิงก์แล้ว"))
                .catch(() => toast.error("คัดลอกไม่สำเร็จ — คัดลอกจากช่องที่อยู่แทน"))
            }
          >
            <LinkIcon aria-hidden />
            คัดลอกลิงก์
          </Button>
        </div>

        <dl className="flex flex-col gap-1 rounded-lg bg-sunken p-4 text-label">
          <div className="flex items-center justify-between gap-3">
            <dt className="text-muted-foreground">แพ็กเกจ</dt>
            <dd className="font-semibold">
              {session.packageName} · {durationMinutes} นาที
            </dd>
          </div>
          <div className="flex items-center justify-between gap-3">
            <dt className="text-muted-foreground">จำนวน</dt>
            <dd className="font-semibold">
              {session.adults} ผู้ใหญ่{session.children > 0 ? ` ${session.children} เด็ก` : ""}
            </dd>
          </div>
          <div className="flex items-center justify-between gap-3">
            <dt className="text-muted-foreground">เวลา</dt>
            <dd className="font-semibold">
              เปิด <span className="font-mono">{formatTime(session.openedAt)}</span> · หมด{" "}
              <span className="font-mono">{formatTime(session.endsAt)}</span>
            </dd>
          </div>
        </dl>

        <DialogFooter className="border-t-0 bg-transparent">
          <Button type="button" variant="ghost" onClick={onClose}>
            เสร็จสิ้น
          </Button>
          {/* หน้าพิมพ์ QR slip 80mm ของ ticket 20 (`/print/qr/[sessionId]`) เป็นแท็บใหม่ ไม่พิมพ์ในนี้ */}
          <Button asChild size="lg">
            <Link href={`/print/qr/${session.id}`} prefetch={false} target="_blank" rel="noreferrer">
              <PrinterIcon aria-hidden />
              พิมพ์ QR slip
            </Link>
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/** The frame's round −/+ control. The count is a button-free readout between the two buttons. */
function CountStepper({
  id,
  label,
  hint,
  value,
  min,
  onChange,
}: {
  id: string;
  label: string;
  hint: string;
  value: number;
  min: number;
  onChange: (next: number) => void;
}) {
  return (
    <div className="flex items-center justify-between gap-3">
      <span className="flex flex-col">
        <span id={`${id}-label`} className="text-label text-ink-2">
          {label}
        </span>
        <span className="text-xs text-muted-foreground">{hint}</span>
      </span>
      <span className="flex items-center gap-3">
        <Button
          type="button"
          variant="outline"
          size="icon"
          aria-label={`ลด${label}`}
          disabled={value <= min}
          onClick={() => onChange(Math.max(min, value - 1))}
        >
          <MinusIcon aria-hidden />
        </Button>
        <b aria-labelledby={`${id}-label`} className="w-6 text-center text-title tabular-nums">
          {value}
        </b>
        <Button
          type="button"
          size="icon"
          className="rounded-full"
          aria-label={`เพิ่ม${label}`}
          onClick={() => onChange(value + 1)}
        >
          <PlusIcon aria-hidden />
        </Button>
      </span>
    </div>
  );
}
