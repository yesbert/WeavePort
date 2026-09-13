FROM node:24-slim@sha256:2fe369e969550cde8e867afc3fe370b260140cab4a23d467074295b42163d553
ARG PLUGIN_VERSION=1
ENV PLUGIN_VERSION=$PLUGIN_VERSION
WORKDIR /plugin
COPY plugins/typescript/worker.ts .
ENTRYPOINT ["node", "--disable-warning=ExperimentalWarning", "/plugin/worker.ts"]
