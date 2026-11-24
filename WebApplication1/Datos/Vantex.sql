-- VantexNova: base exclusiva del prototipo. No modifica bases existentes ajenas.
-- La aplicación puede crear esta base al iniciar si tu usuario tiene permiso.
IF DB_ID(N'VantexNova') IS NULL CREATE DATABASE VantexNova;
GO
USE VantexNova;
GO
-- Identidad y hash de contraseña de cada alumno.
IF OBJECT_ID('Usuarios') IS NULL CREATE TABLE Usuarios(
 Id uniqueidentifier PRIMARY KEY, Nombre nvarchar(80) NOT NULL,
 Email nvarchar(160) NOT NULL UNIQUE, PasswordHash nvarchar(500) NOT NULL);
-- Saldo virtual y ganancias o pérdidas ya realizadas.
IF OBJECT_ID('Cuentas') IS NULL CREATE TABLE Cuentas(
 UsuarioId uniqueidentifier PRIMARY KEY REFERENCES Usuarios(Id),
 Saldo decimal(19,4) NOT NULL CHECK(Saldo>=0), Realizado decimal(19,4) NOT NULL);
-- Cantidades y costo de los activos que el alumno conserva por modo.
IF OBJECT_ID('Posiciones') IS NULL CREATE TABLE Posiciones(
 UsuarioId uniqueidentifier REFERENCES Usuarios(Id), Simbolo nvarchar(10), Modo nvarchar(30),
 Cantidad decimal(28,8) NOT NULL CHECK(Cantidad>0), Costo decimal(19,4) NOT NULL CHECK(Costo>=0),
 PRIMARY KEY(UsuarioId,Simbolo,Modo));
-- Historial de compras y ventas virtuales por usuario y solicitud.
IF OBJECT_ID('Operaciones') IS NULL CREATE TABLE Operaciones(
 Id uniqueidentifier NOT NULL, UsuarioId uniqueidentifier REFERENCES Usuarios(Id),
 Simbolo nvarchar(10) NOT NULL, Tipo nvarchar(10) NOT NULL,
 Cantidad decimal(28,8) NOT NULL, Precio decimal(19,8) NOT NULL, Total decimal(19,4) NOT NULL,
 Resultado decimal(19,4) NOT NULL, Fuente nvarchar(100) NOT NULL, Modo nvarchar(30) NOT NULL, Fecha datetimeoffset NOT NULL, PRIMARY KEY(Id,UsuarioId));
-- Objetivos de ahorro con la tasa y aportaciones que eligió el alumno.
IF OBJECT_ID('Metas') IS NULL CREATE TABLE Metas(
 Id uniqueidentifier PRIMARY KEY, UsuarioId uniqueidentifier REFERENCES Usuarios(Id),
 Nombre nvarchar(80) NOT NULL, Costo decimal(19,4) NOT NULL, Inicial decimal(19,4) NOT NULL,
 Mensual decimal(19,4) NOT NULL, TasaAnual float NOT NULL);
-- Lecciones completadas. La llave compuesta impide registrarlas dos veces.
IF OBJECT_ID('Progreso') IS NULL CREATE TABLE Progreso(
 UsuarioId uniqueidentifier REFERENCES Usuarios(Id), LeccionId nvarchar(30),
 PRIMARY KEY(UsuarioId,LeccionId));
GO
-- Consultas para practicar (ejecutar después de registrar un usuario y operar):
SELECT Nombre, Email FROM Usuarios;
SELECT u.Nombre, c.Saldo FROM Usuarios u JOIN Cuentas c ON c.UsuarioId=u.Id;
SELECT Simbolo, Tipo, Cantidad, Precio, Total, Modo, Fecha FROM Operaciones ORDER BY Fecha DESC;


