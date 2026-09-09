-- Выполняется Postgres один раз при инициализации тома: вторая база для Flow.Auth
-- (Identity + OpenIddict) рядом с основной "flow". Владелец — тот же пользователь flow.
CREATE DATABASE flow_auth;
