@echo off
set enlistmentroot=%~dp0
doskey pubwin=dotnet publish -r win-x64 -p:PublishSingleFile=true --self-contained true $*
doskey msb=dotnet build $*
doskey root=pushd %enlistmentroot%
doskey tst=pushd %enlistmentroot%src\Tests\$*
doskey srv=pushd %enlistmentroot%src\Server\$*
