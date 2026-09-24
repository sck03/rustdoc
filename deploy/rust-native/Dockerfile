# syntax=docker/dockerfile:1.7
FROM node:26.10.0-trixie-slim AS web
WORKDIR /workspace
ENV npm_config_cache=/workspace/.cache/npm
COPY apps/export-doc-web/package.json apps/export-doc-web/package-lock.json apps/export-doc-web/
RUN npm --prefix apps/export-doc-web ci
COPY apps/export-doc-web/ apps/export-doc-web/
RUN npm --prefix apps/export-doc-web run build
COPY scripts/provision-report-fonts.mjs scripts/verify-font-license-policy.mjs scripts/
COPY Resources/Fonts/OpenSource/font-manifest.json Resources/Fonts/OpenSource/OFL-Noto-CJK.txt Resources/Fonts/OpenSource/README.md Resources/Fonts/OpenSource/
RUN node scripts/provision-report-fonts.mjs && node scripts/verify-font-license-policy.mjs --require-files

FROM web AS ocr-assets
ARG TARGETARCH
RUN apt-get update && apt-get install -y --no-install-recommends unzip && rm -rf /var/lib/apt/lists/*
COPY scripts/prepare-native-ocr-runtime.mjs scripts/
COPY scripts/lib/child-process-tree.mjs scripts/lib/
COPY eng/native-runtime-packages.json eng/
RUN case "$TARGETARCH" in amd64) rid=linux-x64;; arm64) rid=linux-arm64;; *) exit 1;; esac \
    && node scripts/prepare-native-ocr-runtime.mjs "$rid" /workspace/artifacts/ocr-runtime

FROM rust:1.98.1-slim-trixie AS rust
WORKDIR /workspace
ENV CARGO_HOME=/workspace/.cache/cargo CARGO_TARGET_DIR=/workspace/target
RUN apt-get update && apt-get install -y --no-install-recommends pkg-config libssl-dev && rm -rf /var/lib/apt/lists/*
COPY Cargo.toml Cargo.lock ./
COPY .cargo/ .cargo/
COPY crates/ crates/
COPY apps/export-doc-server/ apps/export-doc-server/
COPY apps/exportdoc-ocr-rs/ apps/exportdoc-ocr-rs/
COPY apps/export-doc-tauri/src-tauri/ apps/export-doc-tauri/src-tauri/
COPY tools/excel-analyzer-rs/ tools/excel-analyzer-rs/
COPY Resources/ExcelTemplates/ Resources/ExcelTemplates/
COPY Templates/ Templates/
RUN --mount=type=cache,target=/workspace/.cache/cargo/registry,sharing=locked cargo build --locked --release -p export-doc-server \
    && cargo build --locked --release --manifest-path apps/exportdoc-ocr-rs/Cargo.toml

FROM postgres:18.6-trixie AS postgres-client
COPY scripts/lib/copy-postgresql-client.sh /copy-postgresql-client.sh
RUN sh /copy-postgresql-client.sh /client

FROM debian:trixie-slim
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates libssl3t64 && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 exportdoc && useradd --uid 10001 --gid 10001 --no-create-home --home-dir /var/lib/exportdoc exportdoc \
    && mkdir -p /opt/exportdoc /var/lib/exportdoc && chown 10001:10001 /var/lib/exportdoc
WORKDIR /opt/exportdoc
COPY --from=rust /workspace/target/release/export-doc-server ./ExportDocManager.Server
COPY --from=postgres-client /client/ ./Tools/PostgreSQL/
COPY --from=rust /workspace/target/release/exportdoc-ocr ./sidecar/ocr/exportdoc-ocr
COPY --from=ocr-assets /workspace/artifacts/ocr-runtime/ocr/ ./sidecar/ocr/
COPY --from=ocr-assets /workspace/artifacts/ocr-runtime/pdf/ ./Resources/Pdf/
COPY OcrModels/PaddleOCR/V6/ ./OcrModels/PaddleOCR/V6/
COPY apps/exportdoc-ocr-rs/README.md ./sidecar/ocr/README.md
COPY --from=web /workspace/apps/export-doc-web/dist/ ./Web/
COPY --from=web /workspace/Resources/Fonts/OpenSource/ ./Resources/Fonts/OpenSource/
COPY Resources/ExcelTemplates/ ./Resources/ExcelTemplates/
COPY Templates/ ./Templates/
COPY THIRD_PARTY_NOTICES.md THIRD_PARTY_DEPENDENCIES.md ./
USER 10001:10001
EXPOSE 5188
STOPSIGNAL SIGTERM
ENTRYPOINT ["/opt/exportdoc/ExportDocManager.Server"]
CMD ["--app-root","/opt/exportdoc","--data-root","/var/lib/exportdoc","--web-root","/opt/exportdoc/Web","--bind","0.0.0.0:5188"]
