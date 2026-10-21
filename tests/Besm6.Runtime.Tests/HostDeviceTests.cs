using Besm6.Architecture.Numerics;
using Besm6.Processor.Memory;
using Besm6.Runtime.Devices;
using Besm6.Runtime.Loading;
using Besm6.Runtime.Storage;

namespace Besm6.Runtime.Tests;

[TestClass]
public sealed class HostDeviceTests
{
    [TestMethod]
    [DataRow(PuncherTextFormat.Braille)]
    [DataRow(PuncherTextFormat.Grid)]
    [DataRow(PuncherTextFormat.Ascii)]
    public void PuncherFormatsPreserveThePhysicalCard(PuncherTextFormat format)
    {
        string directory = Path.Combine(Path.GetTempPath(), "besm6-card-format-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var memory = new CoreMemory(32768);
        var card = PunchCardCodec.Encode("A=B+3", PunchCardEncoding.Ibm);
        byte[] bytes = card.ToMemoryImage();
        for (int i = 0; i < 24; i++)
        {
            ulong word = 0;
            for (int j = 0; j < 6; j++) word = (word << 8) | bytes[i * 6 + j];
            memory.Write((uint)(100 + i), new Word48(word));
        }
        var puncher = new Puncher(memory, directory) { TextFormat = format };
        PunchCardImage? emitted = null;
        puncher.CardPunched += image => emitted = image;
        try
        {
            puncher.Punch(100, 124); puncher.Finish();
            string expected = format == PuncherTextFormat.Braille ? PunchCardRenderer.RenderBraille(card)
                : PunchCardRenderer.RenderGrid(card, PunchCardEncoding.Ibm, format == PuncherTextFormat.Ascii);
            Assert.AreEqual(expected, File.ReadAllText(Path.Combine(directory, "punch.out")));
            Assert.IsNotNull(emitted);
            CollectionAssert.AreEqual(bytes, emitted.ToMemoryImage());
        }
        finally { puncher.Finish(); Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void TeletypeHostSinkAndInputPreserveCharacterOrder()
    {
        var device = new TeletypeDevice(1);
        var output = new System.Text.StringBuilder(); device.Output = text => output.Append(text);
        device.SendInput("АБ\n");
        foreach (char character in "АБ\n")
        {
            var word = device.ReadCharacter(); Assert.AreEqual((ulong)character, word.Value); device.Write(word);
        }
        Assert.AreEqual(0UL, device.Read().Value);
        Assert.AreEqual("АБ\n", device.GetFullOutput());
        Assert.AreEqual("АБ\n", output.ToString());
    }

    [TestMethod]
    [DataRow("Tape", 25)]
    [DataRow("Disk", 26)]
    [DataRow("Drum", 2)]
    public void ExplicitMountsRetainReadOnlyAndGuestTransferSemantics(string kind, int unit)
    {
        var machine = new MachineCore(); var loader = new DubnaLoader(machine);
        var writable = new TapeImage(123, new byte[TapeImage.PageNbytes], false); writable.WriteWord(0, 0x123456789ABC);
        loader.MountImage(kind, unit, writable);
        Assert.AreSame(writable, loader.GetMountedImage(kind, unit));
        Assert.Throws<InvalidOperationException>(() => loader.MountImage(kind, unit, writable));
        writable.ReadToMemory(machine.Memory, 0, 0, 1024, 1);
        Assert.AreEqual(0x123456789ABCUL, machine.Memory.Read(1024).Value);
        var readOnly = new TapeImage(456, (byte[])writable.Data.Clone());
        loader.MountImage(kind, unit, readOnly, replace: true);
        machine.Memory.Write(1024, new Word48(7)); readOnly.WriteFromMemory(machine.Memory, 0, 0, 1024, 1);
        Assert.AreEqual(0x123456789ABC, readOnly.ReadWord(0));
        loader.MountImage(kind, unit, writable, replace: true);
        writable.WriteFromMemory(machine.Memory, 0, 0, 1024, 1);
        Assert.AreEqual(7L, writable.ReadWord(0));
        loader.UnmountImage(kind, unit);
        Assert.IsNull(loader.GetMountedImage(kind, unit));
    }
}
