import type { FormEventHandler } from "react";
import { ArrowRight, FileText, LockKeyhole, LogIn, Server, ShieldCheck, UserRound } from "lucide-react";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import type { ProductEditionPresentation } from "../../app/productEdition.ts";

type LoginPageProps = {
  apiBaseUrl: string;
  username: string;
  password: string;
  bootstrapToken: string;
  isDesktopRuntime: boolean;
  isBusy: boolean;
  message: string | null;
  product: ProductEditionPresentation;
  onApiBaseUrlChange: (value: string) => void;
  onUsernameChange: (value: string) => void;
  onPasswordChange: (value: string) => void;
  onBootstrapTokenChange: (value: string) => void;
  onSubmit: FormEventHandler<HTMLFormElement>;
};

export function LoginPage({
  apiBaseUrl,
  username,
  password,
  bootstrapToken,
  isDesktopRuntime,
  isBusy,
  message,
  product,
  onApiBaseUrlChange,
  onUsernameChange,
  onPasswordChange,
  onBootstrapTokenChange,
  onSubmit,
}: LoginPageProps) {
  return (
    <main className="login-screen">
      <div className="login-grid-overlay" aria-hidden="true" />
      <div className="login-composition">
        <section className="login-brand-copy" aria-label="系统名称">
          <div className="login-brand-lockup">
            <span className="login-app-icon">
              <FileText size={28} aria-hidden="true" />
            </span>
            <span className="login-brand-lockup-copy">
              <strong>{product.productName}</strong>
              <small>{product.englishName}</small>
            </span>
          </div>
          <div className="login-title-row">
            <h1>{product.productName}</h1>
            <span className="login-edition-badge">{product.editionName}</span>
          </div>
          <p>{product.loginTagline}</p>
        </section>

        <form className="login-card" onSubmit={onSubmit} onKeyDownCapture={handleEnterAsTabFormKeyDown}>
          <div className="login-card-header">
            <div>
              <p className="login-kicker">工作区</p>
              <h2>登录</h2>
              <p className="login-card-subtitle">使用管理员分配的账号进入业务工作台</p>
            </div>
            <span className="login-card-mark">
              <LogIn size={20} aria-hidden="true" />
            </span>
          </div>

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

          {!isDesktopRuntime ? (
            <details className="login-connection-settings">
              <summary>高级连接选项</summary>
              <label className="login-field">
                <span>服务器地址</span>
                <span className="login-input-shell">
                  <Server size={17} aria-hidden="true" />
                  <input
                    aria-label="业务服务器地址"
                    value={apiBaseUrl}
                    onChange={(event) => onApiBaseUrlChange(event.target.value)}
                    spellCheck={false}
                  />
                </span>
              </label>
              <small>通常无需修改；仅在管理员要求切换业务服务器时填写。</small>
              <label className="login-field">
                <span>首次启用口令</span>
                <span className="login-input-shell">
                  <ShieldCheck size={17} aria-hidden="true" />
                  <input
                    aria-label="首次启用口令"
                    value={bootstrapToken}
                    onChange={(event) => onBootstrapTokenChange(event.target.value)}
                    type="password"
                    autoComplete="off"
                    maxLength={512}
                    placeholder="仅首次启用系统时填写"
                    spellCheck={false}
                  />
                </span>
              </label>
              <small>由系统部署人员提供，仅首次建立管理员账号时使用；登录成功后会立即清除。</small>
            </details>
          ) : null}
        </form>
      </div>
    </main>
  );
}
