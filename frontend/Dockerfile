FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build -- --configuration production

FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/frontend/browser /usr/share/nginx/html
COPY scripts/replace_env.sh /docker-entrypoint.d/40-replace-env.sh
RUN chmod +x /docker-entrypoint.d/40-replace-env.sh
EXPOSE 80
