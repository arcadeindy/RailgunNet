﻿/*
 *  RailgunNet - A Client/Server Network State-Synchronization Layer for Games
 *  Copyright (c) 2016 - Alexander Shoulson - http://ashoulson.com
 *
 *  This software is provided 'as-is', without any express or implied
 *  warranty. In no event will the authors be held liable for any damages
 *  arising from the use of this software.
 *  Permission is granted to anyone to use this software for any purpose,
 *  including commercial applications, and to alter it and redistribute it
 *  freely, subject to the following restrictions:
 *  
 *  1. The origin of this software must not be misrepresented; you must not
 *     claim that you wrote the original software. If you use this software
 *     in a product, an acknowledgment in the product documentation would be
 *     appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be
 *     misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
*/

using System;
using System.Collections;
using System.Collections.Generic;

namespace Railgun
{
  public class BitPacker
  {
    // This class works with both uint-based and byte-based storage,
    // so you can adjust these constants accordingly with data type
    private const int  SIZE_INPUT   = sizeof(uint) * 8;
    private const int  SIZE_STORAGE = sizeof(uint) * 8;
    private const uint STORAGE_MASK = (uint)((1L << SIZE_STORAGE) - 1);

    private const int  DEFAULT_CAPACITY = 8;

    private static int ClampBits(int bits)
    {
      if (bits < 0)
        return 0;
      if (bits > BitPacker.SIZE_INPUT)
        return BitPacker.SIZE_INPUT;
      return bits;
    }

    /// <summary>
    /// The position of the next-to-be-written bit.
    /// </summary>
    private int position;

    /// <summary>
    /// Buffer for storing data.
    /// </summary>
    private uint[] data;

    /// <summary>
    /// The number of bits currently stored in the buffer.
    /// </summary>
    public int BitsUsed { get { return this.position; } }

    /// <summary>
    /// Capacity is in data chunks: uint = 4 bytes
    /// </summary>
    public BitPacker(int capacity)
    {
      this.data = new uint[capacity];
      this.Clear();
    }

    public BitPacker() : this(BitPacker.DEFAULT_CAPACITY) { }

    public void Clear()
    {
      for (int i = 0; i < this.data.Length; i++)
        this.data[i] = 0;
      this.position = 0;
    }

    /// <summary>
    /// Takes the lower numBits from the value and stores them in the buffer.
    /// </summary>
    public void Push(uint value, int numBits)
    {
      numBits = BitPacker.ClampBits(numBits);

      while (numBits > 0)
      {
        // Find our place
        int index = this.position / BitPacker.SIZE_STORAGE;
        int used = this.position % BitPacker.SIZE_STORAGE;

        // Increase our capacity if needed
        if (index >= this.data.Length)
          RailgunUtil.ExpandArray(ref this.data);

        // Create and apply the mask
        ulong mask = (1UL << numBits) - 1;
        uint masked = value & (uint)mask;
        uint entry = masked << used;

        // Record how much was written and shift the value
        int remaining = BitPacker.SIZE_STORAGE - used;
        int written = (numBits < remaining) ? numBits : remaining;
        value >>= written;

        // Store and advance
        this.data[index] |= entry;
        this.position += written;
        numBits -= written;
      }
    }

    /// <summary>
    /// Pushes an encodable value.
    /// </summary>
    internal void Push<T>(Encoder<T> encoder, T value)
    {
      uint encoded = encoder.Pack(value);
      this.Push(encoded, encoder.RequiredBits);
    }

    /// <summary>
    /// Pops the top numBits from the buffer and returns them as the lowest
    /// order bits in the return value.
    /// </summary>
    public uint Pop(int numBits)
    {
      numBits = BitPacker.ClampBits(numBits);
      if (numBits > this.position)
        throw new AccessViolationException("BitPacker access underrun");

      uint output = 0;
      while (numBits > 0)
      {
        // Find the position of the last written bit
        int lastWritten = this.position - 1;
        int index = lastWritten / BitPacker.SIZE_STORAGE;
        int used = (lastWritten % BitPacker.SIZE_STORAGE) + 1;

        // Create the mask and extract the value
        int available = (numBits < used) ? numBits : used;
        // Lower mask cuts out any data lower in the stack
        int ignoreBottom = used - available;
        uint mask = STORAGE_MASK << ignoreBottom;

        // Extract the value and flash the bits out of the data
        uint value = (this.data[index] & mask) >> ignoreBottom;
        this.data[index] &= ~mask;

        // Update our position
        numBits -= available;
        this.position -= available;

        // Merge the resulting value
        output |= value << numBits;
      }

      return output;
    }

    /// <summary>
    /// Pops a value and decodes it.
    /// </summary>
    internal T Pop<T>(Encoder<T> encoder)
    {
      uint data = this.Pop(encoder.RequiredBits);
      return encoder.Unpack(data);
    }

    /// <summary>
    /// Pops the top numBits from the buffer and returns them as the lowest
    /// order bits in the return value.
    /// </summary>
    public uint Peek(int numBits)
    {
      int startingPosition = this.position;
      numBits = BitPacker.ClampBits(numBits);
      uint output = 0;

      while (numBits > 0)
      {
        // Find the position of the last written bit
        int lastWritten = this.position - 1;
        int index = lastWritten / BitPacker.SIZE_STORAGE;
        int used = (lastWritten % BitPacker.SIZE_STORAGE) + 1;

        // Create the mask and extract the value
        int available = (numBits < used) ? numBits : used;
        // Lower mask cuts out any data lower in the stack
        int ignoreBottom = used - available;
        uint mask = STORAGE_MASK << ignoreBottom;

        // Extract the value, but don't flash out the data
        uint value = (this.data[index] & mask) >> ignoreBottom;

        // Update our position
        numBits -= available;
        this.position -= available;

        // Merge the resulting value
        output |= value << numBits;
      }

      this.position = startingPosition;
      return output;
    }

    /// <summary>
    /// Peeks at a value and decodes it.
    /// </summary>
    internal T Peek<T>(Encoder<T> encoder)
    {
      uint data = this.Peek(encoder.RequiredBits);
      return encoder.Unpack(data);
    }

    #region Conditional Serialization Helpers
    internal void PushIf<T>(
      int flags,
      int requiredFlag,
      Encoder<T> encoder,
      T value)
    {
      if ((flags & requiredFlag) == requiredFlag)
        this.Push(encoder, value);
    }

    internal T PopIf<T>(
      int flags,
      int requiredFlag,
      Encoder<T> encoder,
      T basisVal)
    {
      if ((flags & requiredFlag) == requiredFlag)
        return this.Pop(encoder);
      return basisVal;
    }
    #endregion
  }
}
