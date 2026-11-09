/// <summary>
/// 네트워크로 주고받는 메시지의 타입 문자열 모음. "타입|인자1|인자2" 형식으로 한 줄씩 보냄.
/// </summary>
public static class NetMsg
{
    public const string WeaponStrike = "WEAPON_STRIKE"; // source card ID, target lane, global turn
    public const string Seeking = "SEEKING";       // 아직 호스트/클라이언트를 못 정한 상태로 브로드캐스트
    public const string Host = "HOST";             // 호스트로 정해진 뒤 계속 브로드캐스트(재접속 대비)
    public const string GameStart = "GAME_START";  // 호스트가 선공/후공을 정해서 알려줄 때
    public const string EndTurn = "END_TURN";      // 턴 종료
    public const string CoinUsed = "COIN_USED";    // 후공 동전 1회 사용
    public const string SpellCast = "SPELL_CAST";  // 마법 사용 (카드ID, 대상 라인 또는 -1)
    public const string DrawConfirmed = "DRAW_CONFIRMED";
    public const string CardPlayed = "CARD_PLAYED"; // 카드 낸 것 (카드ID, 라인 인덱스)
    public const string Rematch = "REMATCH";       // 재대결 요청 - 받은 쪽(특히 호스트)이 게임을 새로 초기화함
}
