# syntax=docker/dockerfile:1.7

# Build context is src/web (see azure.yaml).
FROM node:22-alpine AS build
WORKDIR /src
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# Hub URL is relative — nginx in the runtime stage proxies /hubs/* to the API
# Container App, so the browser sees same-origin requests.
ENV VITE_HUB_URL=/hubs/terminal
RUN npm run build

FROM nginx:1.27-alpine AS runtime
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /src/dist /usr/share/nginx/html
# Bicep overrides API_HOST with the API's internal FQDN. The default value lets
# `docker run` against a sibling container named "api" work without env wiring.
ENV API_HOST=api
EXPOSE 80
