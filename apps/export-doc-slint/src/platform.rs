//! Desktop integration boundary. UI-thread-owned handles stay here; business
//! services and file workers receive only values and explicitly selected paths.
use copypasta::{ClipboardContext, ClipboardProvider};
use std::path::PathBuf;

#[derive(Default)]
pub struct DesktopPlatform {
    clipboard: Option<ClipboardOwner>,
}

struct ClipboardOwner {
    provider: Box<dyn ClipboardProvider>,
    // Drop the provider before the display borrowed by the Wayland backend.
    #[cfg(target_os = "linux")]
    _display: slint::WindowHandle,
}

impl DesktopPlatform {
    fn clipboard(
        &mut self,
        window: &slint::Window,
    ) -> Result<&mut Box<dyn ClipboardProvider>, String> {
        if self.clipboard.is_none() {
            self.clipboard = Some(create_clipboard(window)?);
        }
        self.clipboard
            .as_mut()
            .map(|owner| &mut owner.provider)
            .ok_or_else(|| "系统剪贴板初始化失败。".into())
    }
    pub fn copy_text(&mut self, window: &slint::Window, text: String) -> Result<(), String> {
        self.clipboard(window)?
            .set_contents(text)
            .map_err(|error| format!("无法写入剪贴板：{error}"))
    }
    pub fn paste_text(&mut self, window: &slint::Window) -> Result<String, String> {
        self.clipboard(window)?
            .get_contents()
            .map_err(|error| format!("无法读取剪贴板文本：{error}"))
    }
    pub fn choose_pdf_destination(&self, window: &slint::Window, name: &str) -> Option<PathBuf> {
        self.choose_destination(window, name, &["pdf"])
    }
    pub fn choose_excel_source(&self, window: &slint::Window) -> Option<PathBuf> {
        self.choose_source(
            window,
            "Excel 工作簿",
            &["xlsx", "xlsm", "xltx", "xltm", "xls"],
        )
    }
    pub fn choose_pdf_sources(&self, window: &slint::Window) -> Option<Vec<PathBuf>> {
        rfd::FileDialog::new()
            .set_parent(&window.window_handle())
            .add_filter("PDF 文件", &["pdf"])
            .pick_files()
    }
    pub fn choose_source(
        &self,
        window: &slint::Window,
        label: &str,
        extensions: &[&str],
    ) -> Option<PathBuf> {
        let parent = window.window_handle();
        rfd::FileDialog::new()
            .set_parent(&parent)
            .add_filter(label, extensions)
            .pick_file()
    }
    pub fn choose_destination(
        &self,
        window: &slint::Window,
        name: &str,
        extensions: &[&str],
    ) -> Option<PathBuf> {
        // rfd selects the native Windows/macOS dialog or Linux portal. Calling
        // from the UI thread also meets AppKit's event-loop requirement.
        let parent = window.window_handle();
        rfd::FileDialog::new()
            .set_parent(&parent)
            .set_file_name(name)
            .add_filter("导出文件", extensions)
            .save_file()
    }
    pub fn choose_directory(&self, window: &slint::Window) -> Option<PathBuf> {
        rfd::FileDialog::new()
            .set_parent(&window.window_handle())
            .pick_folder()
    }
}

fn create_clipboard(_window: &slint::Window) -> Result<ClipboardOwner, String> {
    #[cfg(target_os = "linux")]
    {
        use raw_window_handle::{HasDisplayHandle, RawDisplayHandle};
        let display = _window.window_handle();
        let handle = display
            .display_handle()
            .map_err(|error| error.to_string())?;
        let provider: Box<dyn ClipboardProvider> = match handle.as_raw() {
            RawDisplayHandle::Wayland(wayland) => {
                // SAFETY: This is Slint's live Wayland connection. ClipboardOwner
                // retains its owned handle until after the clipboard drops.
                let (_, clipboard) = unsafe {
                    copypasta::wayland_clipboard::create_clipboards_from_external(
                        wayland.display.as_ptr(),
                    )
                };
                Box::new(clipboard)
            }
            RawDisplayHandle::Xlib(_) | RawDisplayHandle::Xcb(_) => {
                Box::new(ClipboardContext::new().map_err(|error| error.to_string())?)
            }
            _ => return Err("当前 Linux 显示系统没有可用的剪贴板适配器。".into()),
        };
        Ok(ClipboardOwner {
            provider,
            _display: display,
        })
    }
    #[cfg(not(target_os = "linux"))]
    {
        Ok(ClipboardOwner {
            provider: Box::new(
                ClipboardContext::new().map_err(|error| format!("系统剪贴板不可用：{error}"))?,
            ),
        })
    }
}

impl DesktopPlatform {
    pub fn paste_image(&self) -> Result<crate::worker::files::ImageFrame, String> {
        let mut clipboard =
            arboard::Clipboard::new().map_err(|cause| format!("系统图片剪贴板不可用：{cause}"))?;
        let image = clipboard
            .get_image()
            .map_err(|cause| format!("剪贴板没有可读取的图片：{cause}"))?;
        if image.width == 0
            || image.height == 0
            || image.width > 16384
            || image.height > 16384
            || image
                .width
                .checked_mul(image.height)
                .is_none_or(|n| n > 40_000_000)
            || image.bytes.len() != image.width * image.height * 4
        {
            return Err("剪贴板图片超过 OCR 容量范围。".into());
        }
        Ok(crate::worker::files::ImageFrame {
            width: image.width as u32,
            height: image.height as u32,
            rgba: image.bytes.into_owned(),
        })
    }
}
