# Delegation

인터페이스 구현 위임과 대상 멤버 노출을 제공하는 C# 소스 제네레이터 모음입니다.

| 패키지 | 역할 |
| --- | --- |
| [Macaron.InterfaceDelegation](InterfaceDelegation/README.md) | 지정한 인터페이스 계약의 구현을 내부 대상에 위임 |
| [Macaron.MemberExposure](MemberExposure/README.md) | 대상의 멤버를 선택하여 속한 타입의 API로 노출 |

## 패키지 생성하기

```powershell
dotnet pack ./InterfaceDelegation/InterfaceDelegation.csproj -c Release
dotnet pack ./MemberExposure/MemberExposure.csproj -c Release
```

결과는 각각 `InterfaceDelegation/bin/Release`와 `MemberExposure/bin/Release`에 생성됩니다.
