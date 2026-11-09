using System;

// Match-local values allow old/new balance comparisons without editing global state.
[Serializable]
public sealed class MatchRuleSet
{
    public int heroHealth = GameRules.StartingHeroHealth;
    public int maxHandSize = GameRules.MaxHandSize;
    public bool permanentBuff = true;
    public bool coinExceedsCap = true;
    public bool attackDamageToHero = true;
    public int openingHand = 6;
    public int cardsPerTurn = 1;
    public int drawSpellCards = 3;
    public int healSpellAmount = 4;
    public int removeSpellDamage = GameRules.RemoveSpellDamage;
    public bool globalClear = true;
    public int sweepSpellDamage = 2;
    public int buffSpellAttack = GameRules.BuffSpellAttackBonus;
    public bool fatigueOnEmptyDraw = true;

    public static MatchRuleSet Legacy() => new MatchRuleSet
    {
        heroHealth = 10, maxHandSize = 9, permanentBuff = false, coinExceedsCap = false,
        attackDamageToHero = false, openingHand = 0,
        cardsPerTurn = 0, drawSpellCards = 1, healSpellAmount = 1,
        removeSpellDamage = 4, sweepSpellDamage = 0, globalClear = false, buffSpellAttack = 2,
        fatigueOnEmptyDraw = false
    };
}
