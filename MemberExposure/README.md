# MemberExposure

`[Expose]` 어트리뷰트를 사용하여 대상의 멤버를 바깥 타입의 API로 노출하는 C# 소스 제네레이터입니다.

```csharp
using Macaron.MemberExposure;

public sealed class Worker
{
    public string Name => "worker";

    public void Run() { }
}

public partial class Wrapper
{
    [Expose(filter: new[] { "Name", "Run" }, rename: new[] { "Run:Execute" })]
    private readonly Worker _worker = new();
}

// 생성되는 코드
partial class Wrapper
{
    public string Name { get => _worker.Name; }

    public void Execute() => _worker.Run();
}
```

| 매개변수 | 설명 |
| --- | --- |
| filter | 지정한 이름의 멤버만 포함. 생략하거나 빈 배열이면 모든 멤버가 대상 |
| remove | filter 적용 후 지정한 이름을 제외 |
| rename | `기존이름:새이름` 형식으로 출력 이름 변경 |

- 멤버의 선택은 이름만을 고려하며 멤버의 종류나 오버로딩 여부는 고려하지 않습니다.
- 위임 가능한 대상은 public/internal로 선언된 메서드, 프로퍼티(인덱서 제외), 이벤트입니다.
- `[Expose(IncludeBaseTypes = true)]`로 노출 대상 타입의 기반 타입 멤버까지 포함할 수 있습니다. 기본값은 `false`입니다.
- rename이 적용된 이름으로 기존 멤버와의 충돌 여부를 판정합니다.
