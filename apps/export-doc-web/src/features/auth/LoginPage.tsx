import type { FormEventHandler } from "react";
import { ArrowRight, FileText, LockKeyhole, LogIn, Server, UserRound } from "lucide-react";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { LoginBackgroundScene } from "./LoginBackgroundScene.tsx";
import type { ProductEditionPresentation } from "../../app/productEdition.ts";

type LoginPageProps = {
  apiBaseUrl: string;
  username: string;
  password: string;
  isBusy: boolean;
  message: string | null;
  product: ProductEditionPresentation;
  onApiBaseUrlChange: (value: string) => void;
  onUsernameChange: (value: string) => void;
  onPasswordChange: (value: string) => void;
  onSubmit: FormEventHandler<HTMLFormElement>;
};

export function LoginPage({
  apiBaseUrl,
  username,
  password,
  isBusy,
  message,
  product,
  onApiBaseUrlChange,
  onUsernameChange,
  onPasswordChange,
  onSubmit,
}: LoginPageProps) {
  return (
    <main className="login-screen">
      <LoginBackgroundScene />
      <div className="login-grid-overlay" aria-hidden="true" />
      <div className="login-composition">
        <section className="login-brand-copy" aria-label="系统名称">
          <div className="login-brand-lockup">
            <span className="login-app-icon">
              <FileText size={28} aria-hidden="true" />
            </span>
            <span>{product.englishName}</span>
          </div>
          <h1>{product.productName}</h1>
          <p>{product.loginTagline} · {product.editionName}</p>
        </section>

        <form className="login-card" onSubmit={onSubmit} onKeyDownCapture={handleEnterAsTabFormKeyDown}>
          <div className="login-card-header">
            <div>
              <p className="login-kicker">工作区</p>
              <h2>登录</h2>
            </div>
            <span className="login-card-mark">
              <LogIn size={20} aria-hidden="true" />
            </span>
          </div>

          <label className="login-field">
            <span>API 地址</span>
            <span className="login-input-shell">
              <Server size={17} aria-hidden="true" />
              <input value={apiBaseUrl} onChange={(event) => onApiBaseUrlChange(event.target.value)} />
            </span>
          </label>

          <label className="login-field">
            <span>账号</span>
            <span className="login-input-shell">
              <UserRound size={17} aria-hidden="true" />
              <input value={username} onChange={(event) => onUsernameChange(event.target.value)} autoComplete="username" />
            </span>
          </label>

          <label className="login-field">
            <span>密码</span>
            <span className="login-input-shell">
              <LockKeyhole size={17} aria-hidden="true" />
              <input
                value={password}
                onChange={(event) => onPasswordChange(event.target.value)}
                type="password"
                autoComplete="current-password"
                autoFocus
              />
            </span>
          </label>

          {message ? (
            <div className="login-alert" role="alert">
              {message}
            </div>
          ) : null}

          <button className="login-submit-button" type="submit" disabled={isBusy} aria-busy={isBusy}>
            <span>{isBusy ? "正在登录" : "登录"}</span>
            <ArrowRight size={18} aria-hidden="true" />
          </button>
        </form>
      </div>
    </main>
  );
}
