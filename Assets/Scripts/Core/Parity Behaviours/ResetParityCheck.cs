using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ResetParityCheck : IParityMethod
{
    public bool UpsideDown { get { return _upsideDown; } }
    private bool _upsideDown;

    // Returns true if the inputted note and bomb coordinates cause a reset potentially
    private Dictionary<int, Func<Vector2, int, int, Parity, bool>> _bombDetectionConditions = new()
    {
        { 0, (note, x, y, parity) => ((y >= note.y && y != 0) || (y > note.y && y > 0)) && x == note.x },
        { 1, (note, x, y, parity) => ((y <= note.y && y != 2) || (y < note.y && y < 2)) && x == note.x },
        { 2, (note, x, y, parity) => (parity == Parity.Forehand && (y == note.y || y == note.y - 1) && ((note.x != 0 && x < note.x) || (note.x > 0 && x <= note.x))) ||
            (parity == Parity.Backhand && y == note.y && ((note.x != 0 && x < note.x) || (note.x > 0 && x <= note.x))) },
        { 3, (note, x, y, parity) => (parity == Parity.Forehand && (y == note.y || y == note.y - 1) && ((note.x != 3 && x > note.x) || (note.x < 3 && x >= note.x))) ||
            (parity == Parity.Backhand && y == note.y && ((note.x != 3 && x > note.x) || (note.x < 3 && x >= note.x))) },
        { 4, (note, x, y, parity) => ((y >= note.y && y != 0) || (y > note.y && y > 0)) && x == note.x },
        { 5, (note, x, y, parity) => ((y >= note.y && y != 0) || (y > note.y && y > 0)) && x == note.x },
        { 6, (note, x, y, parity) => ((y <= note.y && y != 2) || (y < note.y && y < 2)) && x == note.x },
        { 7, (note, x, y, parity) => ((y <= note.y && y != 2) || (y < note.y && y < 2)) && x == note.x },
        { 8, (note,x,y, parity) => false }
    };

    public bool BombResetCheck(BeatCutData lastCut, List<BombNote> bombs)
    {
        // Not found yet
        bool bombReset = false;
        for (int i = 0; i < bombs.Count; i++)
        {
            // Get current bomb
            BombNote bomb = bombs[i];
            ColourNote note;

            // If in the center 2 grid spaces, no point trying
            if ((bomb.x == 1 || bomb.x == 2) && bomb.y == 1) continue;

            // Get the last note. In the case of a stack, picks the note that isnt at 2 or 0 as
            // it triggers a reset when it shouldn't.
            note = lastCut.notesInCut.Where(note => note.x == lastCut.endPositioning.x && note.y == lastCut.endPositioning.y).FirstOrDefault();

            // Get the last notes cut direction based on the last swings angle
            var lastNoteCutDir = (lastCut.sliceParity == Parity.Forehand) ?
                SliceMap.ForehandDict.FirstOrDefault(x => x.Value == Math.Round(lastCut.endPositioning.angle / 45.0) * 45).Key :
                SliceMap.BackhandDict.FirstOrDefault(x => x.Value == Math.Round(lastCut.endPositioning.angle / 45.0) * 45).Key;

            // Offset the checking if the entire outerlane bombs indicate moving inwards
            int xOffset = 0;

            bool bombOffsetting = bombs.Any(bomb => bomb.x == note.x && (bomb.y <= note.y && lastCut.sliceParity == Parity.Backhand && lastCut.endPositioning.angle >= 0)) ||
                bombs.Any(bomb => bomb.x == note.x && (bomb.y >= note.y && lastCut.sliceParity == Parity.Forehand && lastCut.endPositioning.angle >= 0));

            if (bombOffsetting && note.x == 0) xOffset = 1;
            if (bombOffsetting && note.x == 3) xOffset = -1;

            // Determine if lastnote and current bomb cause issue
            // If we already found reason to reset, no need to try again
            bombReset = _bombDetectionConditions[lastNoteCutDir](new Vector2(note.x + xOffset, note.y), bomb.x, bomb.y, lastCut.sliceParity);
            if (bombReset) return true;
        }
        return false;
    }

    public Parity ParityCheck(BeatCutData lastCut, ref BeatCutData currentSwing, List<BombNote> bombs, float playerXOffset, bool rightHand)
    {
        // AFN: Angle from neutral
        // Assuming a forehand down hit is neutral, and a backhand up hit
        // Rotating the hand inwards goes positive, and outwards negative
        // Using a list of definitions, turn cut direction into an angle, and check
        // if said angle makes sense.

        ColourNote nextNote = currentSwing.notesInCut[0];

        float currentAFN = (lastCut.sliceParity != Parity.Forehand) ?
            SliceMap.BackhandDict[lastCut.notesInCut[0].d] :
            SliceMap.ForehandDict[lastCut.notesInCut[0].d];

        int orient = nextNote.d;
        if (nextNote.d == 8) orient = (lastCut.sliceParity == Parity.Forehand) ?
                 SliceMap.ForehandDict.FirstOrDefault(x => x.Value == Math.Round(lastCut.endPositioning.angle / 45.0) * 45).Key :
                 SliceMap.BackhandDict.FirstOrDefault(x => x.Value == Math.Round(lastCut.endPositioning.angle / 45.0) * 45).Key;

        float nextAFN = (lastCut.sliceParity == Parity.Forehand) ?
            SliceMap.BackhandDict[orient] :
            SliceMap.ForehandDict[orient];

        float angleChange = currentAFN - nextAFN;
        _upsideDown = false;

        // Check for potential bomb resets
        bool bombReset = BombResetCheck(lastCut, bombs);

        if (bombReset)
        {
            // Set as bomb reset and return same parity as last swing
            currentSwing.resetType = ResetType.Bomb;
            return (lastCut.sliceParity == Parity.Forehand) ? Parity.Forehand : Parity.Backhand;
        }

        // If note is a dot, play it as a down hit
        if (nextNote.d == 8 && nextNote.y == 0) { return Parity.Forehand; }
        // If note is a left or right hit in the outerlane, hit as down
        if ((nextNote.d == 3 || nextNote.d == 4) && (nextNote.x == 0 || nextNote.x == 3) && (nextNote.y == 0)) { return Parity.Forehand; }

        // If the angle change exceeds 180 then triangle
        if (Mathf.Abs(angleChange) > 90)
        {
            currentSwing.resetType = ResetType.Normal;
            return (lastCut.sliceParity == Parity.Forehand) ? Parity.Forehand : Parity.Backhand;
        }
        else { return (lastCut.sliceParity == Parity.Forehand) ? Parity.Backhand : Parity.Forehand; }
    }
}
