use crate::{attachment::Attachment, content, recipient};
use lettre::{
    AsyncSmtpTransport, AsyncTransport, Message, Tokio1Executor,
    message::{MultiPart, SinglePart, header::ContentType},
    transport::smtp::{
        authentication::Credentials,
        client::{Tls, TlsParameters},
    },
};
use std::time::{Duration, Instant};
use zeroize::Zeroizing;

pub struct Config {
    pub host: String,
    pub port: u16,
    pub tls: bool,
    pub user: String,
    pub password: Zeroizing<String>,
    pub from: String,
    pub display_name: String,
}
pub fn message(
    config: &Config,
    to: &str,
    subject: &str,
    html: &str,
    attachments: Vec<Attachment>,
) -> Result<Message, String> {
    if config.host.trim().is_empty()
        || config.host.len() > 253
        || config
            .host
            .chars()
            .any(|c| c.is_control() || c.is_whitespace())
        || config.port == 0
    {
        return Err("SMTP 服务器或端口无效，请先保存邮件设置。".into());
    }
    if subject.chars().count() > 300 || subject.contains(['\r', '\n']) {
        return Err("邮件主题超过 300 字或包含换行。".into());
    }
    if config.display_name.chars().any(char::is_control) {
        return Err("发件人姓名包含无效字符。".into());
    }
    let mut from = recipient::mailbox(&config.from)?;
    if !config.display_name.is_empty() {
        from.name = Some(config.display_name.clone());
    }
    let to = recipient::mailbox(to)?;
    let (html, plain) = content::sanitize(html)?;
    let alternative = MultiPart::alternative()
        .singlepart(SinglePart::plain(plain))
        .singlepart(SinglePart::html(html));
    let builder = Message::builder().from(from).to(to).subject(subject);
    if attachments.is_empty() {
        return builder
            .multipart(alternative)
            .map_err(|_| "邮件内容无法编码。".into());
    }
    if attachments.len() > crate::attachment::MAX_COUNT
        || attachments.iter().map(|a| a.bytes.len()).sum::<usize>() > crate::attachment::MAX_TOTAL
    {
        return Err("邮件附件最多 10 个，总计不超过 18 MiB。".into());
    }
    let mut mixed = MultiPart::mixed().multipart(alternative);
    for attachment in attachments {
        let mime = ContentType::parse(attachment.mime).map_err(|_| "邮件附件类型无效。")?;
        mixed = mixed.singlepart(
            lettre::message::Attachment::new(attachment.name).body(attachment.bytes, mime),
        );
    }
    builder
        .multipart(mixed)
        .map_err(|_| "邮件内容无法编码。".into())
}

pub enum Failure {
    Cancelled(String),
    Timeout,
    Unavailable,
}
impl Failure {
    pub fn message(&self) -> String {
        match self {
            Self::Cancelled(message) => message.clone(),
            Self::Timeout => "邮件投递超过时限，请先查询投递记录再决定是否重发。".into(),
            Self::Unavailable => {
                "邮件服务暂时不可用，请检查 SMTP、TLS、登录信息和网络连接。".into()
            }
        }
    }
}
pub fn send(
    config: &Config,
    message: Message,
    check: &dyn Fn() -> Result<(), String>,
) -> Result<(), Failure> {
    check().map_err(Failure::Cancelled)?;
    let mut builder = AsyncSmtpTransport::<Tokio1Executor>::builder_dangerous(&config.host)
        .port(config.port)
        .timeout(Some(Duration::from_secs(20)));
    if config.tls {
        let tls = TlsParameters::new(config.host.clone()).map_err(|_| Failure::Unavailable)?;
        builder = builder.tls(if config.port == 465 {
            Tls::Wrapper(tls)
        } else {
            Tls::Required(tls)
        });
    }
    if !config.user.trim().is_empty() && !config.password.is_empty() {
        builder = builder.credentials(Credentials::new(
            config.user.clone(),
            config.password.to_string(),
        ));
    }
    let transport = builder.build();
    let runtime = tokio::runtime::Builder::new_current_thread()
        .enable_all()
        .build()
        .map_err(|_| Failure::Unavailable)?;
    runtime.block_on(async{
        let delivery=transport.send(message);tokio::pin!(delivery);let start=Instant::now();
        loop{tokio::select!{
            result=&mut delivery=>return result.map(|_|()).map_err(|_|Failure::Unavailable),
            _=tokio::time::sleep(Duration::from_millis(40))=>{check().map_err(Failure::Cancelled)?;if start.elapsed()>Duration::from_secs(60){return Err(Failure::Timeout);}},
        }}
    })
}
