# Build from the repository root, like the other services:
#   docker build -f src/AutoRemediator.Web/Dockerfile -t <image> .
# Run with API_URL set to the API's base URL; nginx proxies /api to it.
FROM node:24-alpine AS build
WORKDIR /src
COPY src/AutoRemediator.Web/package.json src/AutoRemediator.Web/package-lock.json ./
RUN npm ci
COPY src/AutoRemediator.Web/ ./
RUN npm run build

FROM nginx:stable-alpine AS final
COPY src/AutoRemediator.Web/nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /src/dist /usr/share/nginx/html
EXPOSE 8080
