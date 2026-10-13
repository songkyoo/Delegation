# InterfaceDelegation

`Implement` 어트리뷰트를 사용하여 인터페이스 구현을 멤버에 위임하는 C# 소스 제네레이터입니다.

## Implement 어트리뷰트

필드, 참조 타입 프로퍼티(값 타입은 불가) 또는 primary constructors의 매개변수에 사용할 수 있습니다.

```csharp
using Macaron.InterfaceDelegation;

public interface IRunner
{
    void Run();
}

public sealed class Runner
{
    public void Run() { }
}

public partial class Wrapper : IRunner
{
    [Implement(typeof(IRunner))]
    private readonly Runner _runner = new();
}

// 생성되는 코드
partial class Wrapper : IRunner
{
    void IRunner.Run() => _runner.Run();
}
```

- `Implement` 어트리뷰트를 사용하는 타입과 이를 감싸는 모든 중첩 타입은 `partial`이어야 합니다.
- 대상이 해당 인터페이스를 구현하지 않아도 호환되는 멤버를 가지고 있다면 사용할 수 있습니다.
- 인터페이스 타입을 명시하려면 `[Implement(typeof(IRunner))]`처럼 어트리뷰트의 첫 번째 인자로 타입을 지정합니다. 열린 제네릭 타입은 사용할 수 없습니다.
- 인터페이스 타입 인자를 생략하면 대상 멤버의 선언 타입이 인터페이스일 때 그 타입을 사용합니다. 대상 타입이 직접 구현한 인터페이스가 정확히 하나라면 때 그 인터페이스를 사용합니다. 직접 구현한 인터페이스가 없거나 둘 이상이면 타입을 명시해야 합니다.

## 구현 모드

위임할 인터페이스는 명시적이거나 암시적으로 구현할 수 있으며 `Implement` 어트리뷰트의 `Mode` 프로퍼티에 `ImplementationMode` 열거형을 지정하여 결정할 수 있습니다.

- 생성할 멤버의 이름이 바깥 타입 이름과 같으면 명시적 인터페이스 구현을 사용합니다.
- Auto와 Explicit 모드는 인터페이스 멤버에 대해서 이미 동일한 명시적 구현이 있다면 코드를 생성하지 않습니다. 명시적이지 않은 동일한 구현은 명시적 구현을 만들 때 무시합니다. Implicit 모드에서는 기존 암시적·명시적 구현을 모두 고려합니다.

```csharp
[Implement(typeof(IRunner), Mode = ImplementationMode.Implicit)]
private readonly Runner _runner = new();
```

타입 자체가 인터페이스이거나 구현하는 인터페이스가 하나인 경우 인터페이스 타입 인자를 생략할 수 있습니다. 다음 예제는 생성자로 구현 객체를 전달받습니다.

```csharp
public partial class ImplicitWrapper(IRunner runner) : IRunner
{
    [Implement(Mode = ImplementationMode.Implicit)]
    private readonly IRunner _runner = runner;
}
```

- `Auto`(기본값): 기본적으로 명시적 인터페이스 구현을 생성합니다. 기반 타입의 일치하는 `abstract` 멤버가 있고 현재 타입에 구현이 없다면 그 멤버를 `override`합니다. 이미 명시적 인터페이스 구현이 있다면 생성하지 않습니다.
- `Explicit`: 명시적 인터페이스 구현을 생성합니다. 인터페이스 멤버에 대해서 명시적 구현이 있다면 생성하지 않고, 암시적 구현이 있다면 생성합니다.
- `Implicit`: public 구현을 생성합니다. 인터페이스 멤버에 대해서 이미 암시적 또는 명시적 구현이 있다면 생성하지 않습니다. 기반 타입의 abstract 멤버는 가능하다면 오버라이드 멤버를 생성합니다.
