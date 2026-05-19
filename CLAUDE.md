# CLAUDE.md

이 파일은 이 저장소에서 작업할 때 Claude Code(claude.ai/code)에게 제공되는 가이드입니다.

## 프로젝트 개요

WotrBuildSync는 *패스파인더: 의인의 분노(Pathfinder: Wrath of the Righteous)* 캐릭터 빌드를 import/export하기 위한 C# 콘솔 애플리케이션입니다. [WOTR Codex](https://wotr-codex.vercel.app/) 웹사이트와 연동하여 빌드를 저장하고 불러올 수 있습니다.

## 빌드 명령어

.NET Framework 4.8 프로젝트입니다. MSBuild 또는 JetBrains Rider IDE를 통해 빌드합니다.

```powershell
# Debug 빌드
msbuild WotrBuildSync.sln /p:Configuration=Debug

# Release 빌드
msbuild WotrBuildSync.sln /p:Configuration=Release
```

빌드 결과물은 `WotrBuildSync\bin\Debug\` 또는 `WotrBuildSync\bin\Release\`에 생성됩니다.

현재 테스트 및 린트 도구는 설정되어 있지 않습니다.

## 아키텍처

프로젝트는 초기 개발 단계로, `Program.cs`가 유일한 소스 파일이며 `Main()`은 비어 있습니다. 예정된 데이터 흐름은 다음과 같습니다:

1. 게임에서 캐릭터 빌드 데이터 읽기 (세이브 파일 또는 인메모리 게임 상태)
2. WOTR Codex 웹사이트와 호환되는 형식으로 빌드 직렬화/변환
3. Import: 사이트에서 빌드를 가져와 게임에 적용
4. Export: 현재 인게임 빌드를 사이트에 업로드

루트 네임스페이스는 `WotrBuildSync`입니다. 대상 프레임워크: `net48` (레거시 .NET Framework, .NET Core 아님). 의존성은 표준 BCL만 사용 (System, System.Core, System.Data, System.Xml) — NuGet 패키지 없음.

상세 아키텍쳐: docs/architecture.md