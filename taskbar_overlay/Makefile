# Makefile for TaskbarOverlay
# Extracted from TrafficMonitor's taskbar window code — pure Win32, no MFC.
#
# Supports two compilers:
#   make           — auto-detects: prefers cl.exe (MSVC) if available, falls back to g++ (MinGW)
#   make CC=cl     — force MSVC
#   make CC=g++    — force MinGW
#
# Targets:
#   make          — builds taskbar_overlay.exe
#   make clean    — removes build artifacts
#   make run      — builds and runs

# === Compiler selection ======================================================
ifeq ($(CC),)
  CL_FOUND := $(shell which cl 2>/dev/null)
  GPP_FOUND := $(shell which g++ 2>/dev/null)
  ifneq ($(CL_FOUND),)
    CC := cl
  else ifneq ($(GPP_FOUND),)
    CC := g++
  endif
else ifeq ($(CC),cc)
  CL_FOUND := $(shell which cl 2>/dev/null)
  GPP_FOUND := $(shell which g++ 2>/dev/null)
  ifneq ($(CL_FOUND),)
    CC := cl
  else ifneq ($(GPP_FOUND),)
    CC := g++
  endif
endif

# === Output ==================================================================
TARGET = taskbar_overlay.exe
SRCS   = taskbar_overlay.cpp winmain.cpp
HDRS   = taskbar_overlay.h
RC     = app.rc

# === MSVC (cl.exe) configuration =============================================
ifeq ($(CC),cl)
  CXXFLAGS = /nologo /EHsc /W4 /WX /O2 /std:c++17 /UTF-8 \
             /D_UNICODE /DUNICODE /DWIN32 /D_WINDOWS \
             /D_CRT_SECURE_NO_WARNINGS /DNOMINMAX \
             /I.
  LDFLAGS = /nologo /SUBSYSTEM:CONSOLE /MACHINE:X64
  LIBS    = user32.lib gdi32.lib shell32.lib comctl32.lib shcore.lib ws2_32.lib pdh.lib ole32.lib
  CXX = cl
  # Compile .rc to .res with rc.exe
  RES = app.res
  RES_CMD = rc /nologo /fo $(RES) $(RC)

# === MinGW (g++) configuration ===============================================
else ifeq ($(CC),g++)
  CXXFLAGS = -std=c++17 -O2 -Wall -Wextra -Werror -municode \
             -D_UNICODE -DUNICODE -DWIN32 -D_WINDOWS \
             -D_CRT_SECURE_NO_WARNINGS -DNOMINMAX \
             -I.
  # Build as CONSOLE subsystem — app frees console when running as GUI
  LDFLAGS = -mconsole -municode
  LIBS    = -luser32 -lgdi32 -lshell32 -lcomctl32 -lshcore -lws2_32 -lpdh -lole32
  CXX = g++
  # Compile .rc to .o with windres
  RES = app_res.o
  RES_CMD = windres $(RC) -O coff -o $(RES)

else
  $(error "No compiler found. Install MSVC (cl.exe) or MinGW (g++), or specify CC=cl or CC=g++")
endif

# === Build rules =============================================================
.PHONY: all clean run

all: $(TARGET)

# MSVC build rule
ifeq ($(CC),cl)
$(TARGET): $(SRCS) $(HDRS) $(RC)
	$(RES_CMD)
	$(CXX) $(CXXFLAGS) $(SRCS) /Fe:$(TARGET) /link $(LDFLAGS) $(LIBS) $(RES)

# MinGW build rule
else
$(TARGET): $(SRCS) $(HDRS) $(RC)
	$(RES_CMD)
	$(CXX) $(CXXFLAGS) $(SRCS) $(RES) -o $(TARGET) $(LDFLAGS) $(LIBS)
endif

run: $(TARGET)
	./$(TARGET)

clean:
	rm -f $(TARGET) *.obj *.o *.pdb *.ilk *.exp *.lib *.res app_res.o 2>/dev/null
