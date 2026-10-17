"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { CheckIcon, CircleAlertIcon, ImagePlusIcon, Loader2Icon, SoupIcon } from "lucide-react";
import { useEffect, useRef } from "react";
import { useForm, useWatch } from "react-hook-form";
import { toast } from "sonner";
import { z } from "zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { formatBaht } from "@/lib/format";
import type { SettingsDto } from "@/lib/types";
import {
  BANNER_INVALID_MESSAGE,
  bannerRejection,
  useClearBanner,
  useSaveSettings,
  useSettings,
  useUploadBanner,
} from "./settings-api";

/**
 * The client mirror of the server rules (SC/VAT 0–30, the three limits 1–99). Numbers are entered as
 * text because that is what an `<input>` yields: the field keeps whatever was typed (so the live
 * example can follow it) and shows the Thai message as soon as the value leaves its range. The API
 * enforces the same ranges and answers with the same sentence if the two ever disagree.
 *
 * `.trim()` on the optional text fields turns "" into undefined — the server stores that as null,
 * which is what "เว้นว่างได้" for PromptPay ID means.
 */
const settingsSchema = z.object({
  name: z.string().trim().min(1, "กรอกชื่อร้าน").max(100, "ชื่อร้านยาวเกิน 100 ตัวอักษร"),
  address: z.string().trim().max(300, "ที่อยู่ยาวเกิน 300 ตัวอักษร"),
  phone: z.string().trim().max(30, "เบอร์โทรยาวเกิน 30 ตัวอักษร"),
  taxId: z.string().trim().max(30, "เลขผู้เสียภาษียาวเกิน 30 ตัวอักษร").optional(),
  promptPayId: z.string().trim().max(30, "PromptPay ID ยาวเกิน 30 ตัวอักษร").optional(),
  serviceChargePercent: percentField("ค่าบริการ (%)"),
  vatPercent: percentField("VAT (%)"),
  maxQtyPerItemPerOrder: limitField("สูงสุดต่อเมนูต่อรอบ"),
  maxItemsPerOrder: limitField("สูงสุดต่อรอบ"),
  lastOrderWarningMinutes: limitField("เตือน Last order ก่อนหมดเวลา"),
});

/** A percentage 0–30 inclusive (decimals allowed, e.g. 10.5) — the range is written once, here. */
function percentField(label: string) {
  const message = `${label} ต้องอยู่ระหว่าง 0–30`;
  return z
    .string()
    .trim()
    .min(1, message)
    .refine((value) => {
      const parsed = Number(value);
      return Number.isFinite(parsed) && parsed >= 0 && parsed <= 30;
    }, message);
}

/** Whole numbers 1–99 inclusive — the one place the limit range is written on the client. */
function limitField(label: string) {
  const message = `${label} ต้องอยู่ระหว่าง 1–99`;
  return z
    .string()
    .trim()
    .min(1, message)
    .refine((value) => {
      const parsed = Number(value);
      return Number.isInteger(parsed) && parsed >= 1 && parsed <= 99;
    }, message);
}

type SettingsValues = z.infer<typeof settingsSchema>;

/** The live example always starts from ฿1,000 of food (o13-settings), so the SC/VAT effect is readable. */
const EXAMPLE_SUBTOTAL = 1000;

/**
 * What the form starts with before the row arrives (and the shape `reset` restores after a save):
 * the server's own defaults from `Domain.cs` and `Program.cs`, written as the text each input holds.
 */
const EMPTY_SETTINGS: SettingsValues = {
  name: "",
  address: "",
  phone: "",
  taxId: "",
  promptPayId: "",
  serviceChargePercent: "0",
  vatPercent: "0",
  maxQtyPerItemPerOrder: "5",
  maxItemsPerOrder: "20",
  lastOrderWarningMinutes: "15",
};

function toFormValues(settings: SettingsDto): SettingsValues {
  return {
    name: settings.name,
    address: settings.address,
    phone: settings.phone,
    taxId: settings.taxId ?? "",
    promptPayId: settings.promptPayId ?? "",
    serviceChargePercent: String(settings.serviceChargePercent),
    vatPercent: String(settings.vatPercent),
    maxQtyPerItemPerOrder: String(settings.maxQtyPerItemPerOrder),
    maxItemsPerOrder: String(settings.maxItemsPerOrder),
    lastOrderWarningMinutes: String(settings.lastOrderWarningMinutes),
  };
}

/**
 * The example's arithmetic, the same as `BillCalculator`: SC on the subtotal, VAT on subtotal + SC,
 * each rounded away from zero at the second decimal (bill rounding, not float noise).
 */
function exampleBill(serviceChargePercent: string, vatPercent: string) {
  const scPercent = Number(serviceChargePercent);
  const vatPercentValue = Number(vatPercent);
  const sc = Number.isFinite(scPercent) ? scPercent : 0;
  const vat = Number.isFinite(vatPercentValue) ? vatPercentValue : 0;
  const service = round2((EXAMPLE_SUBTOTAL * sc) / 100);
  const vatAmount = round2(((EXAMPLE_SUBTOTAL + service) * vat) / 100);
  return {
    scPercent: sc,
    vatPercent: vat,
    service,
    vat: vatAmount,
    total: EXAMPLE_SUBTOTAL + service + vatAmount,
  };
}

function round2(value: number): number {
  return Math.round((value + Number.EPSILON) * 100) / 100;
}

/**
 * Owner settings (`o13-settings`): shop info, banner, service charge / VAT, order limits, and the live
 * pre-bill example that recomputes from the percentages as they are typed.
 * Nothing on this screen is a timer: the shop's own minutes setting is a plain number field.
 */
export default function SettingsPage() {
  const { data: settings, isPending, error } = useSettings();
  const bannerInput = useRef<HTMLInputElement>(null);

  const {
    register,
    reset,
    control,
    handleSubmit,
    formState: { errors, isDirty, isSubmitting },
  } = useForm<SettingsValues>({
    resolver: zodResolver(settingsSchema),
    mode: "onChange",
    defaultValues: EMPTY_SETTINGS,
  });

  // Load the row once, and again whenever the server answers with a new one (save or banner change).
  // Comparing the serialised row keeps background re-renders from discarding edits the user is typing.
  const loaded = useRef("");
  useEffect(() => {
    if (!settings) return;
    const fingerprint = JSON.stringify(settings);
    if (loaded.current === fingerprint) return;
    loaded.current = fingerprint;
    reset(toFormValues(settings));
  }, [settings, reset]);

  // useWatch (not `watch()`) so React Compiler can memoize this component: the live example only
  // depends on the two percentages the user is typing.
  const [scRaw, vatRaw] = useWatch({
    control,
    name: ["serviceChargePercent", "vatPercent"],
  });
  const example = exampleBill(scRaw, vatRaw);

  const save = useSaveSettings();
  const banner = useUploadBanner();
  const clearBanner = useClearBanner();

  const onSubmit = handleSubmit(async (values) => {
    const saved = await save.mutateAsync({
      name: values.name,
      address: values.address,
      phone: values.phone,
      // Empty optional text is stored as "not set" instead of an empty string.
      taxId: values.taxId?.trim() || undefined,
      promptPayId: values.promptPayId?.trim() || undefined,
      serviceChargePercent: Number(values.serviceChargePercent),
      vatPercent: Number(values.vatPercent),
      maxQtyPerItemPerOrder: Number(values.maxQtyPerItemPerOrder),
      maxItemsPerOrder: Number(values.maxItemsPerOrder),
      lastOrderWarningMinutes: Number(values.lastOrderWarningMinutes),
    });
    reset(toFormValues(saved));
    toast.success("บันทึกการตั้งค่าแล้ว");
  });

  if (isPending) {
    return <SettingsSkeleton />;
  }

  if (error || !settings) {
    return (
      <div className="py-5">
        <p role="alert" className="text-label text-cancel-ink">
          {error instanceof Error ? error.message : "โหลดการตั้งค่าไม่ได้ ลองอีกครั้ง"}
        </p>
      </div>
    );
  }

  return (
    <form onSubmit={onSubmit} noValidate>
      {/* Sticky page head: the title stays put while this long form scrolls (frame o13). */}
      <div className="sticky top-0 z-10 -mx-4 flex min-h-20 flex-wrap items-center justify-between gap-3 border-b border-border bg-background px-4 py-3 lg:-mx-8 lg:px-8">
        <div className="flex flex-col gap-0.5">
          <h1 className="text-headline">ตั้งค่า</h1>
          <span className="text-body text-muted-foreground">ข้อมูลร้าน · การคิดเงิน · การสั่งอาหาร</span>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-label text-muted-foreground">
            {isDirty ? "มีการแก้ไขที่ยังไม่บันทึก" : "บันทึกแล้ว"}
          </span>
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Loader2Icon className="animate-spin" aria-hidden /> : <CheckIcon aria-hidden />}
            บันทึกการตั้งค่า
          </Button>
        </div>
      </div>

      <div className="mt-4 grid items-start gap-4 xl:grid-cols-[1fr_360px]">
        <div className="flex flex-col gap-5 rounded-xl border border-border bg-surface p-4">
          <section className="flex flex-col gap-3.5">
            <div className="flex flex-col gap-1">
              <h2 className="text-title">ข้อมูลร้าน</h2>
              <span className="text-label font-normal text-muted-foreground">
                แสดงบนใบแจ้งยอด ใบเสร็จ และ QR slip
              </span>
            </div>

            <div className="grid gap-x-4 gap-y-3.5 sm:grid-cols-2">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="name">ชื่อร้าน</Label>
                <Input id="name" aria-invalid={errors.name ? true : undefined} {...register("name")} />
                <FieldError name="name" message={errors.name?.message} />
              </div>

              <div className="flex flex-col gap-1.5">
                <Label htmlFor="phone">โทรศัพท์</Label>
                <Input id="phone" inputMode="tel" {...register("phone")} />
                <FieldError name="phone" message={errors.phone?.message} />
              </div>

              <div className="flex flex-col gap-1.5 sm:col-span-2">
                <Label htmlFor="address">ที่อยู่</Label>
                <Input id="address" {...register("address")} />
                <FieldError name="address" message={errors.address?.message} />
              </div>

              <div className="flex flex-col gap-1.5">
                <Label htmlFor="taxId">เลขผู้เสียภาษี</Label>
                <Input id="taxId" {...register("taxId")} />
                <FieldError name="taxId" message={errors.taxId?.message} />
              </div>

              <div className="flex flex-col gap-1.5">
                <Label htmlFor="promptPayId">PromptPay ID</Label>
                <Input id="promptPayId" {...register("promptPayId")} />
                <p className="text-xs text-muted-foreground">ใช้สร้าง QR รับชำระเงิน · เว้นว่างได้</p>
                <FieldError name="promptPayId" message={errors.promptPayId?.message} />
              </div>

              <div className="flex flex-col gap-1.5 sm:col-span-2">
                <Label htmlFor="banner">แบนเนอร์หน้าลูกค้า</Label>
                <div className="flex flex-wrap items-center gap-4">
                  <BannerPreview path={settings.bannerImagePath} />
                  <div className="flex flex-col gap-2">
                    <div className="flex flex-wrap items-center gap-2">
                      <input
                        id="banner"
                        ref={bannerInput}
                        type="file"
                        accept="image/jpeg,image/png,image/webp"
                        className="sr-only"
                        onChange={(event) => {
                          const file = event.target.files?.[0];
                          // Cleared so picking the same file again after a rejection still fires.
                          event.target.value = "";
                          if (!file) return;
                          // The browser already filters by type; the size rule needs this check.
                          const rejection = bannerRejection(file);
                          if (rejection) {
                            toast.error(rejection);
                            return;
                          }
                          banner.mutate(file, { onSuccess: () => toast.success("อัปเดตแบนเนอร์แล้ว") });
                        }}
                      />
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        disabled={banner.isPending}
                        onClick={() => bannerInput.current?.click()}
                      >
                        {banner.isPending ? (
                          <Loader2Icon className="animate-spin" aria-hidden />
                        ) : (
                          <ImagePlusIcon aria-hidden />
                        )}
                        อัปโหลดรูปแบนเนอร์
                      </Button>
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        disabled={!settings.bannerImagePath || clearBanner.isPending}
                        onClick={() => clearBanner.mutate()}
                      >
                        ใช้แบนเนอร์เริ่มต้น
                      </Button>
                    </div>
                    <p className="text-xs text-muted-foreground">
                      jpeg / png / webp ไม่เกิน 2MB · แนะนำ 1170 × 336 พิกเซล · ชื่อร้านแสดงทับรูปเสมอ
                    </p>
                    {banner.isError ? (
                      <p role="alert" className="text-xs text-cancel-ink">
                        {BANNER_INVALID_MESSAGE}
                      </p>
                    ) : null}
                  </div>
                </div>
              </div>
            </div>
          </section>

          <hr className="border-border" />

          <section className="flex flex-col gap-3.5">
            <div className="flex flex-col gap-1">
              <h2 className="text-title">การคิดเงิน</h2>
              <span className="text-label font-normal text-muted-foreground">
                ใช้กับบิลที่ยังไม่ชำระทุกใบ
              </span>
            </div>

            <div className="grid gap-x-4 gap-y-3.5 sm:grid-cols-2">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="serviceChargePercent">ค่าบริการ (%)</Label>
                <Input
                  id="serviceChargePercent"
                  inputMode="decimal"
                  aria-invalid={errors.serviceChargePercent ? true : undefined}
                  {...register("serviceChargePercent")}
                />
                <p className="text-xs text-muted-foreground">ช่วง 0–30% · คิดก่อน VAT</p>
                <FieldError name="serviceChargePercent" message={errors.serviceChargePercent?.message} />
              </div>

              <div className="flex flex-col gap-1.5">
                <Label htmlFor="vatPercent">VAT (%)</Label>
                <Input
                  id="vatPercent"
                  inputMode="decimal"
                  aria-invalid={errors.vatPercent ? true : undefined}
                  {...register("vatPercent")}
                />
                <p className="text-xs text-muted-foreground">ช่วง 0–30% · คิดจากยอด + ค่าบริการ</p>
                <FieldError name="vatPercent" message={errors.vatPercent?.message} />
              </div>
            </div>
          </section>

          <hr className="border-border" />

          <section className="flex flex-col gap-3.5">
            <div className="flex flex-col gap-1">
              <h2 className="text-title">การสั่งอาหาร</h2>
              <span className="text-label font-normal text-muted-foreground">
                ตรวจทั้งฝั่งลูกค้าและเซิร์ฟเวอร์ทุกรอบ
              </span>
            </div>

            <div className="grid gap-x-4 gap-y-3.5 sm:grid-cols-3">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="maxQtyPerItemPerOrder">สูงสุดต่อเมนูต่อรอบ</Label>
                <Input
                  id="maxQtyPerItemPerOrder"
                  inputMode="numeric"
                  aria-invalid={errors.maxQtyPerItemPerOrder ? true : undefined}
                  {...register("maxQtyPerItemPerOrder")}
                />
                <p className="text-xs text-muted-foreground">ช่วง 1–99 · นับแยกตามเมนู</p>
                <FieldError name="maxQtyPerItemPerOrder" message={errors.maxQtyPerItemPerOrder?.message} />
              </div>

              <div className="flex flex-col gap-1.5">
                <Label htmlFor="maxItemsPerOrder">สูงสุดต่อรอบ</Label>
                <Input
                  id="maxItemsPerOrder"
                  inputMode="numeric"
                  aria-invalid={errors.maxItemsPerOrder ? true : undefined}
                  {...register("maxItemsPerOrder")}
                />
                <p className="text-xs text-muted-foreground">ช่วง 1–99 · รวมทุกรายการในรอบ</p>
                <FieldError name="maxItemsPerOrder" message={errors.maxItemsPerOrder?.message} />
              </div>

              <div className="flex flex-col gap-1.5">
                <Label htmlFor="lastOrderWarningMinutes">เตือน Last order ก่อนหมดเวลา</Label>
                <div className="relative">
                  <Input
                    id="lastOrderWarningMinutes"
                    inputMode="numeric"
                    className="pr-14"
                    aria-invalid={errors.lastOrderWarningMinutes ? true : undefined}
                    {...register("lastOrderWarningMinutes")}
                  />
                  <span className="pointer-events-none absolute inset-y-0 right-3.5 flex items-center text-label font-normal text-muted-foreground">
                    นาที
                  </span>
                </div>
                <p className="text-xs text-muted-foreground">ช่วง 1–99 นาที · แสดงฝั่งลูกค้า</p>
                <FieldError
                  name="lastOrderWarningMinutes"
                  message={errors.lastOrderWarningMinutes?.message}
                />
              </div>
            </div>
          </section>
        </div>

        <aside className="flex flex-col gap-3.5 rounded-xl border border-border bg-surface p-4">
          <div className="flex items-center justify-between gap-3">
            <h2 className="text-title">ตัวอย่างการคิดเงิน</h2>
            <span className="text-xs text-muted-foreground">ยอดอาหาร {formatBaht(EXAMPLE_SUBTOTAL)}</span>
          </div>

          <div className="rounded-lg bg-sunken px-3.5 py-3">
            <div className="flex items-center justify-between gap-3 py-1.5 text-body">
              <span className="text-muted-foreground">ยอดก่อนค่าบริการ</span>
              <span className="tabular-nums">{formatBaht(EXAMPLE_SUBTOTAL)}</span>
            </div>
            <div className="flex items-center justify-between gap-3 py-1.5 text-body">
              <span className="text-muted-foreground">ค่าบริการ {example.scPercent}%</span>
              <span className="tabular-nums">{formatBaht(example.service)}</span>
            </div>
            <div className="flex items-center justify-between gap-3 py-1.5 text-body">
              <span className="text-muted-foreground">VAT {example.vatPercent}%</span>
              <span className="tabular-nums">{formatBaht(example.vat)}</span>
            </div>
            <hr className="my-2 border-border" />
            <div className="flex items-center justify-between gap-3 pt-1.5 text-title">
              <span>รวมทั้งสิ้น</span>
              <span className="tabular-nums">{formatBaht(example.total)}</span>
            </div>
          </div>

          <div className="flex flex-col gap-1.5">
            <span className="text-xs text-muted-foreground">
              คำนวณจากเปอร์เซ็นต์ที่ตั้งไว้ · ตัวอย่างนี้ใช้ {example.scPercent}% และ {example.vatPercent}%
            </span>
            <span className="text-xs text-muted-foreground">บิลที่ชำระแล้วจะไม่ถูกคำนวณใหม่</span>
          </div>
        </aside>
      </div>
    </form>
  );
}

/**
 * The banner exactly as the customer screen shows it — shop name over the image with the table and
 * package chips — in the frame's own 390×128 banner scaled to 260×86 (o13-settings).
 */
function BannerPreview({ path }: { path?: string | null }) {
  return (
    <div className="relative w-[260px] flex-none overflow-hidden rounded-lg">
      <div className="relative h-[86px] w-full">
        {path ? (
          // A plain <img>: the banner is an owner upload served by the API at /api/uploads/...,
          // so Next's image optimizer has nothing to do with it.
          // eslint-disable-next-line @next/next/no-img-element
          <img src={path} alt="" className="absolute inset-0 size-full object-cover" />
        ) : (
          <div className="absolute inset-0 bg-orange-strong" />
        )}
        <SoupIcon
          className="absolute -right-2 -bottom-5 size-[100px] text-orange-hover"
          strokeWidth={1.25}
          aria-hidden
        />
        <span className="absolute top-2 left-2 inline-flex h-[18px] items-center rounded-full border border-border bg-surface px-1.5 text-[9px] font-semibold text-ink">
          โต๊ะ A1
        </span>
        <span className="absolute top-2 right-2 inline-flex h-[18px] items-center gap-1 rounded-full border border-border bg-surface px-1.5 text-[9px] font-semibold text-ink">
          <i className="size-1.5 rounded-full bg-orange" />
          Gold · 2 ท่าน
        </span>
        <div className="absolute inset-x-3 top-[38px] text-on-accent">
          <div className="text-[18px] leading-tight font-semibold">ชาบูเดโม่</div>
          <div className="text-[9px] leading-tight">ชาบูบุฟเฟ่ต์ · สั่งอาหารผ่าน QR</div>
        </div>
      </div>
    </div>
  );
}

function FieldError({ name, message }: { name: string; message?: string }) {
  if (!message) return null;
  return (
    <p id={`${name}-error`} className="flex items-center gap-1 text-xs text-cancel-ink">
      <CircleAlertIcon className="size-3.5 flex-none" strokeWidth={1.75} aria-hidden />
      {message}
    </p>
  );
}

function SettingsSkeleton() {
  return (
    <div className="py-5" aria-hidden>
      <Skeleton className="h-9 w-40" />
      <div className="mt-4 grid gap-4 xl:grid-cols-[1fr_360px]">
        <Skeleton className="h-[560px] rounded-xl" />
        <Skeleton className="h-[220px] rounded-xl" />
      </div>
    </div>
  );
}
