"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { CircleAlertIcon, EyeIcon, EyeOffIcon, Loader2Icon, SoupIcon } from "lucide-react";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError, apiFetch } from "@/lib/api";
import { staffHome } from "@/lib/auth";
import type { MeDto } from "@/lib/types";

const loginSchema = z.object({
  email: z
    .string()
    .trim()
    .min(1, "กรอกอีเมล")
    .pipe(z.email("อีเมลไม่ถูกต้อง ตัวอย่าง owner@demo.local")),
  password: z.string().min(1, "กรอกรหัสผ่าน"),
});

type LoginValues = z.infer<typeof loginSchema>;

export function LoginForm() {
  const router = useRouter();
  const [showPassword, setShowPassword] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: "", password: "" },
  });

  const onSubmit = async (values: LoginValues) => {
    setFormError(null);
    try {
      // 204 on success; the auth cookie is set by the response, so the following call is authenticated.
      await apiFetch<void>("/api/auth/login", {
        method: "POST",
        body: JSON.stringify(values),
      });
      const me = await apiFetch<MeDto>("/api/auth/me");
      // Owner lands on the dashboard, Kitchen on the board; the guard in (staff)/layout enforces it.
      router.replace(staffHome(me.role));
    } catch (err) {
      if (err instanceof ApiError) {
        setFormError(err.status === 401 ? "อีเมลหรือรหัสผ่านไม่ถูกต้อง" : err.message);
      } else {
        setFormError("เข้าสู่ระบบไม่สำเร็จ ลองอีกครั้ง");
      }
    }
  };

  return (
    <main className="flex min-h-dvh flex-col px-5 pt-6 pb-7 sm:px-8 lg:px-12 lg:pt-8">
      <div className="flex items-center gap-3">
        <span className="flex size-9 flex-none items-center justify-center rounded-md bg-orange-strong text-on-accent">
          <SoupIcon className="size-5" strokeWidth={1.75} aria-hidden />
        </span>
        <div className="flex flex-col">
          <b className="text-body leading-tight font-semibold">FoodQueue</b>
          <span className="text-xs text-muted-foreground">ระบบสั่งอาหารผ่าน QR</span>
        </div>
      </div>

      {/* Mockup l2 stacks the form right under the brand on a phone; l1 centres it on desktop. */}
      <div className="flex flex-1 flex-col pt-8 sm:justify-center sm:pt-0">
        <div className="flex w-full max-w-[420px] flex-col gap-5 pb-8">
          <h1 className="text-headline lg:text-display">เข้าสู่ระบบ</h1>

          <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
            {formError ? (
              <p
                role="alert"
                className="flex items-start gap-2 rounded-lg border border-cancel-line bg-cancel-soft px-3.5 py-2.5 text-label text-cancel-ink"
              >
                <CircleAlertIcon className="mt-px size-4 flex-none" strokeWidth={1.75} aria-hidden />
                {formError}
              </p>
            ) : null}

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="email">อีเมล</Label>
              <Input
                id="email"
                type="email"
                inputMode="email"
                autoComplete="username"
                autoFocus
                aria-invalid={errors.email ? true : undefined}
                aria-describedby={errors.email ? "email-error" : undefined}
                {...register("email")}
              />
              {errors.email ? (
                <p id="email-error" className="text-xs text-cancel-ink">
                  {errors.email.message}
                </p>
              ) : null}
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="password">รหัสผ่าน</Label>
              <div className="relative">
                <Input
                  id="password"
                  type={showPassword ? "text" : "password"}
                  autoComplete="current-password"
                  className="pr-12"
                  aria-invalid={errors.password ? true : undefined}
                  aria-describedby={errors.password ? "password-error" : undefined}
                  {...register("password")}
                />
                <button
                  type="button"
                  onClick={() => setShowPassword((v) => !v)}
                  aria-label={showPassword ? "ซ่อนรหัสผ่าน" : "แสดงรหัสผ่าน"}
                  className="absolute inset-y-0 right-0 flex w-11 items-center justify-center text-muted-foreground hover:text-ink-2"
                >
                  {showPassword ? (
                    <EyeOffIcon className="size-5" strokeWidth={1.75} aria-hidden />
                  ) : (
                    <EyeIcon className="size-5" strokeWidth={1.75} aria-hidden />
                  )}
                </button>
              </div>
              {errors.password ? (
                <p id="password-error" className="text-xs text-cancel-ink">
                  {errors.password.message}
                </p>
              ) : null}
            </div>

            <div className="mt-1.5 flex flex-col gap-3">
              <Button type="submit" size="lg" className="w-full" disabled={isSubmitting}>
                {isSubmitting ? (
                  <>
                    <Loader2Icon className="size-4 animate-spin" aria-hidden />
                    กำลังเข้าสู่ระบบ
                  </>
                ) : (
                  "เข้าสู่ระบบ"
                )}
              </Button>
              <p className="text-label font-normal text-muted-foreground">
                สำหรับเจ้าของร้านและพนักงานครัว
              </p>
            </div>
          </form>
        </div>
      </div>
    </main>
  );
}
