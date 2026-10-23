#nullable enable
namespace ChainedPuzzles
{
    public class CP_Bioscan_Hud : UnityEngine.Object
    {public bool m_visible=true,m_isClosestToPlayer=true,m_localPlayerInScan=true;public float m_progressTarget=.4f;}
}
namespace Enemies
{
    public class EnemyAgent : UnityEngine.Object
    {public bool Alive=true;public int DimensionIndex;public UnityEngine.Vector3 Position;}
}
