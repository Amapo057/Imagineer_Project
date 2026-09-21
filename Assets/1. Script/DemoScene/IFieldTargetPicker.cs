using System;
using System.Collections.Generic;

/// <summary>
/// "필드 슬롯 하나를 지목해야 하는" 카드 효과(지금은 무기 키워드 하나뿐)가 공통으로 쓰는 대상 선택 창구.
///
/// 카드 효과 쪽 코드(DemoTurnController)는 이 인터페이스만 알고 있으면 되고, 실제로 "어떻게" 대상을
/// 고르는지는 구현체한테 전부 맡김:
/// - 지금(PC 목업): MouseFieldTargetPicker — 유효 대상 슬롯 중 하나를 마우스로 클릭
/// - 나중(VR): 손으로 실물 카드를 터치/가리키는 구현체로 교체 — 이 인터페이스와 DemoTurnController 쪽은
///   전혀 건드릴 필요 없이, 구현체(컴포넌트)만 인스펙터에서 바꿔 끼우면 됨
/// </summary>
public interface IFieldTargetPicker
{
    /// <summary>
    /// validTargets 중 하나를 플레이어가 고르면 onTargetChosen에 그 슬롯을 넘겨서 호출함.
    /// 선택이 취소되거나(예: 우클릭) 애초에 고를 수 있는 대상이 없으면 onTargetChosen(null)을 호출함.
    /// 구현체는 결과가 나올 때까지 시간이 걸릴 수 있음(플레이어 입력을 기다리므로) — 즉시 반환되지 않아도 됨
    /// </summary>
    void RequestTarget(List<FieldSlot> validTargets, Action<FieldSlot> onTargetChosen);
}
