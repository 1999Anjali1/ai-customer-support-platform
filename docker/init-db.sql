-- Runs once when the PostgreSQL container is first created.
-- Enables pgvector before the API connects, so Npgsql loads the "vector" type on its first connection.
CREATE EXTENSION IF NOT EXISTS vector;
