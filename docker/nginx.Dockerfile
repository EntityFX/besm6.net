FROM nginx:stable-alpine
COPY docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY src/Besm6.Landing/ /usr/share/nginx/html/
